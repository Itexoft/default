// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Itexoft.IO;
using Itexoft.IO.Streams.Chars;
using Itexoft.Net.Http;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.UI.Web.EmbeddedWeb;

internal sealed class EmbeddedWebRpcDispatcher
{
    private readonly object handler;
    private readonly Dictionary<string, EmbeddedWebRpcMethod> methods;

    public EmbeddedWebRpcDispatcher(object handler)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        this.methods = BuildMethodMap(handler.GetType());

        if (this.methods.Count == 0)
            throw new InvalidOperationException("RPC handler must define at least one method.");
    }

    public NetHttpResponse Dispatch(string methodName, NetHttpRequest request, CancelToken cancelToken)
    {
        if (!IsPost(request.Method))
            return new NetHttpResponse(NetHttpStatus.MethodNotAllowed);

        if (string.IsNullOrWhiteSpace(methodName))
            return new NetHttpResponse(NetHttpStatus.NotFound);

        if (!this.methods.TryGetValue(methodName, out var method))
            return new NetHttpResponse(NetHttpStatus.NotFound);

        if (request.Length > 0 && !IsJsonContentType(request.Headers.ContentType))
            return new NetHttpResponse(NetHttpStatus.BadRequest);

        try
        {
            return method.Invoke(this.handler, new CharStreamBr(request.Content!, Encoding.UTF8), cancelToken);
        }
        catch (JsonException)
        {
            return new NetHttpResponse(NetHttpStatus.BadRequest);
        }
    }

    private static bool IsJsonContentType(NetHttpContentType contentType) =>
        contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);

    private static bool IsPost(NetHttpMethod method) =>
        method.Value.ToString().Equals("POST", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, EmbeddedWebRpcMethod> BuildMethodMap(Type handlerType)
    {
        var methods = new Dictionary<string, EmbeddedWebRpcMethod>(StringComparer.OrdinalIgnoreCase);
        var candidates = handlerType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

        foreach (var method in candidates)
        {
            var attribute = method.GetCustomAttribute<EmbeddedWebRpcMethodAttribute>(true);

            if (attribute is null)
                continue;

            if (method.IsStatic)
                throw new InvalidOperationException($"RPC method '{method.Name}' must be an instance method.");

            if (method.ContainsGenericParameters)
                throw new InvalidOperationException($"RPC method '{method.Name}' must not be generic.");

            var name = string.IsNullOrWhiteSpace(attribute.Name) ? method.Name : attribute.Name!;

            ValidateMethodName(name, method);

            if (!methods.TryAdd(name, CreateMethod(handlerType, method, name)))
                throw new InvalidOperationException($"RPC method name '{name}' is already registered.");
        }

        return methods;
    }

    private static void ValidateMethodName(string name, MethodInfo method)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"RPC method '{method.Name}' has an empty name.");

        if (name.Contains('/'))
            throw new InvalidOperationException($"RPC method '{method.Name}' cannot contain '/'.");
    }

    private static EmbeddedWebRpcMethod CreateMethod(Type handlerType, MethodInfo method, string name)
    {
        var parameters = method.GetParameters();
        var hasToken = parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancelToken);
        var argCount = parameters.Length - (hasToken ? 1 : 0);

        if (argCount < 0 || argCount > 1)
            throw new InvalidOperationException($"RPC method '{method.Name}' must accept at most one argument.");

        foreach (var parameter in parameters)
        {
            if (parameter.ParameterType.IsByRef || parameter.IsOut)
                throw new InvalidOperationException($"RPC method '{method.Name}' cannot use ref or out parameters.");
        }

        var argsType = argCount == 1 ? parameters[0].ParameterType : null;
        var returnInfo = GetReturnInfo(method.ReturnType);

        if (argsType is null)
            return CreateNoArgsMethod(handlerType, method, name, hasToken, returnInfo);

        return CreateArgsMethod(handlerType, argsType, method, name, hasToken, returnInfo);
    }

    private static EmbeddedWebRpcMethod CreateArgsMethod(
        Type handlerType,
        Type argsType,
        MethodInfo method,
        string name,
        bool hasToken,
        RpcReturnInfo returnInfo)
    {
        var factory = returnInfo.IsVoid
            ? typeof(EmbeddedWebRpcDispatcher).GetMethod(nameof(CreateArgsVoidMethodCore), BindingFlags.NonPublic | BindingFlags.Static)
            : typeof(EmbeddedWebRpcDispatcher).GetMethod(nameof(CreateArgsMethodCore), BindingFlags.NonPublic | BindingFlags.Static);

        return returnInfo.IsVoid
            ? (EmbeddedWebRpcMethod)factory!.MakeGenericMethod(handlerType, argsType).Invoke(null, [method, name, hasToken, returnInfo.Kind])!
            : (EmbeddedWebRpcMethod)factory!.MakeGenericMethod(handlerType, argsType, returnInfo.ResultType)
                .Invoke(null, [method, name, hasToken, returnInfo.Kind])!;
    }

    private static EmbeddedWebRpcMethod CreateNoArgsMethod(Type handlerType, MethodInfo method, string name, bool hasToken, RpcReturnInfo returnInfo)
    {
        var factory = returnInfo.IsVoid
            ? typeof(EmbeddedWebRpcDispatcher).GetMethod(nameof(CreateNoArgsVoidMethodCore), BindingFlags.NonPublic | BindingFlags.Static)
            : typeof(EmbeddedWebRpcDispatcher).GetMethod(nameof(CreateNoArgsMethodCore), BindingFlags.NonPublic | BindingFlags.Static);

        return returnInfo.IsVoid
            ? (EmbeddedWebRpcMethod)factory!.MakeGenericMethod(handlerType).Invoke(null, [method, name, hasToken, returnInfo.Kind])!
            : (EmbeddedWebRpcMethod)factory!.MakeGenericMethod(handlerType, returnInfo.ResultType)
                .Invoke(null, [method, name, hasToken, returnInfo.Kind])!;
    }

    private static EmbeddedWebRpcMethod CreateArgsMethodCore<THandler, TArgs, TResult>(
        MethodInfo method,
        string name,
        bool hasToken,
        RpcReturnKind kind) =>
        new EmbeddedWebRpcArgsMethod<THandler, TArgs, TResult>(name, CreateArgsHandler<THandler, TArgs, TResult>(method, hasToken, kind), false);

    private static EmbeddedWebRpcMethod CreateArgsVoidMethodCore<THandler, TArgs>(
        MethodInfo method,
        string name,
        bool hasToken,
        RpcReturnKind kind) =>
        new EmbeddedWebRpcArgsMethod<THandler, TArgs, RpcVoid>(name, CreateArgsVoidHandler<THandler, TArgs>(method, hasToken, kind), true);

    private static EmbeddedWebRpcMethod CreateNoArgsMethodCore<THandler, TResult>(
        MethodInfo method,
        string name,
        bool hasToken,
        RpcReturnKind kind) =>
        new EmbeddedWebRpcNoArgsMethod<THandler, TResult>(name, CreateNoArgsHandler<THandler, TResult>(method, hasToken, kind), false);

    private static EmbeddedWebRpcMethod CreateNoArgsVoidMethodCore<THandler>(MethodInfo method, string name, bool hasToken, RpcReturnKind kind) =>
        new EmbeddedWebRpcNoArgsMethod<THandler, RpcVoid>(name, CreateNoArgsVoidHandler<THandler>(method, hasToken, kind), true);

    private static Func<THandler, TArgs, CancelToken, TResult> CreateArgsHandler<THandler, TArgs, TResult>(
        MethodInfo method,
        bool hasToken,
        RpcReturnKind kind) =>
        (h, a, c) => CreateArgsHandlerAsync<THandler, TArgs, TResult>(method, hasToken, kind).Invoke(h, a, c).GetAwaiter().GetResult();

    private static Func<THandler, TArgs, CancelToken, Promise<TResult>> CreateArgsHandlerAsync<THandler, TArgs, TResult>(
        MethodInfo method,
        bool hasToken,
        RpcReturnKind kind)
    {
        if (hasToken)
        {
            return kind switch
            {
                RpcReturnKind.Promise => (Func<THandler, TArgs, CancelToken, Promise<TResult>>)method.CreateDelegate(
                    typeof(Func<THandler, TArgs, CancelToken, Promise<TResult>>)),
                RpcReturnKind.Task => WrapTask(
                    (Func<THandler, TArgs, CancelToken, Task<TResult>>)method.CreateDelegate(
                        typeof(Func<THandler, TArgs, CancelToken, Task<TResult>>))),
                RpcReturnKind.ValueTask => WrapValueTask(
                    (Func<THandler, TArgs, CancelToken, ValueTask<TResult>>)method.CreateDelegate(
                        typeof(Func<THandler, TArgs, CancelToken, ValueTask<TResult>>))),
                RpcReturnKind.Sync => WrapSync(
                    (Func<THandler, TArgs, CancelToken, TResult>)method.CreateDelegate(typeof(Func<THandler, TArgs, CancelToken, TResult>))),
                _ => throw new InvalidOperationException("Unsupported RPC return kind."),
            };
        }

        return kind switch
        {
            RpcReturnKind.Promise => WrapNoToken(
                (Func<THandler, TArgs, Promise<TResult>>)method.CreateDelegate(typeof(Func<THandler, TArgs, Promise<TResult>>))),
            RpcReturnKind.Task => WrapTask((Func<THandler, TArgs, Task<TResult>>)method.CreateDelegate(typeof(Func<THandler, TArgs, Task<TResult>>))),
            RpcReturnKind.ValueTask => WrapValueTask(
                (Func<THandler, TArgs, ValueTask<TResult>>)method.CreateDelegate(typeof(Func<THandler, TArgs, ValueTask<TResult>>))),
            RpcReturnKind.Sync => WrapSync((Func<THandler, TArgs, TResult>)method.CreateDelegate(typeof(Func<THandler, TArgs, TResult>))),
            _ => throw new InvalidOperationException("Unsupported RPC return kind."),
        };
    }

    private static Func<THandler, CancelToken, TResult>
        CreateNoArgsHandler<THandler, TResult>(MethodInfo method, bool hasToken, RpcReturnKind kind) =>
        (h, c) => CreateNoArgsHandlerAsync<THandler, TResult>(method, hasToken, kind).Invoke(h, c).GetAwaiter().GetResult();

    private static Func<THandler, CancelToken, Promise<TResult>> CreateNoArgsHandlerAsync<THandler, TResult>(
        MethodInfo method,
        bool hasToken,
        RpcReturnKind kind)
    {
        if (hasToken)
        {
            return kind switch
            {
                RpcReturnKind.Promise => (Func<THandler, CancelToken, Promise<TResult>>)method.CreateDelegate(
                    typeof(Func<THandler, CancelToken, Promise<TResult>>)),
                RpcReturnKind.Task => WrapTask(
                    (Func<THandler, CancelToken, Task<TResult>>)method.CreateDelegate(typeof(Func<THandler, CancelToken, Task<TResult>>))),
                RpcReturnKind.ValueTask => WrapValueTask(
                    (Func<THandler, CancelToken, ValueTask<TResult>>)method.CreateDelegate(typeof(Func<THandler, CancelToken, ValueTask<TResult>>))),
                RpcReturnKind.Sync => WrapSync(
                    (Func<THandler, CancelToken, TResult>)method.CreateDelegate(typeof(Func<THandler, CancelToken, TResult>))),
                _ => throw new InvalidOperationException("Unsupported RPC return kind."),
            };
        }

        return kind switch
        {
            RpcReturnKind.Promise => WrapNoToken((Func<THandler, Promise<TResult>>)method.CreateDelegate(typeof(Func<THandler, Promise<TResult>>))),
            RpcReturnKind.Task => WrapTask((Func<THandler, Task<TResult>>)method.CreateDelegate(typeof(Func<THandler, Task<TResult>>))),
            RpcReturnKind.ValueTask => WrapValueTask(
                (Func<THandler, ValueTask<TResult>>)method.CreateDelegate(typeof(Func<THandler, ValueTask<TResult>>))),
            RpcReturnKind.Sync => WrapSync((Func<THandler, TResult>)method.CreateDelegate(typeof(Func<THandler, TResult>))),
            _ => throw new InvalidOperationException("Unsupported RPC return kind."),
        };
    }

    private static Func<THandler, TArgs, CancelToken, RpcVoid> CreateArgsVoidHandler<THandler, TArgs>(
        MethodInfo method,
        bool hasToken,
        RpcReturnKind kind) =>
        (h, a, c) => CreateArgsVoidHandlerAsync<THandler, TArgs>(method, hasToken, kind).Invoke(h, a, c).GetAwaiter().GetResult();

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> CreateArgsVoidHandlerAsync<THandler, TArgs>(
        MethodInfo method,
        bool hasToken,
        RpcReturnKind kind)
    {
        if (hasToken)
        {
            return kind switch
            {
                RpcReturnKind.Promise => WrapVoid(
                    (Func<THandler, TArgs, CancelToken, Promise>)method.CreateDelegate(typeof(Func<THandler, TArgs, CancelToken, Promise>))),
                RpcReturnKind.Task => WrapVoid(
                    (Func<THandler, TArgs, CancelToken, Task>)method.CreateDelegate(typeof(Func<THandler, TArgs, CancelToken, Task>))),
                RpcReturnKind.ValueTask => WrapVoid(
                    (Func<THandler, TArgs, CancelToken, ValueTask>)method.CreateDelegate(typeof(Func<THandler, TArgs, CancelToken, ValueTask>))),
                RpcReturnKind.Sync => WrapVoid(
                    (Action<THandler, TArgs, CancelToken>)method.CreateDelegate(typeof(Action<THandler, TArgs, CancelToken>))),
                _ => throw new InvalidOperationException("Unsupported RPC return kind."),
            };
        }

        return kind switch
        {
            RpcReturnKind.Promise => WrapVoid((Func<THandler, TArgs, Promise>)method.CreateDelegate(typeof(Func<THandler, TArgs, Promise>))),
            RpcReturnKind.Task => WrapVoid((Func<THandler, TArgs, Task>)method.CreateDelegate(typeof(Func<THandler, TArgs, Task>))),
            RpcReturnKind.ValueTask => WrapVoid((Func<THandler, TArgs, ValueTask>)method.CreateDelegate(typeof(Func<THandler, TArgs, ValueTask>))),
            RpcReturnKind.Sync => WrapVoid((Action<THandler, TArgs>)method.CreateDelegate(typeof(Action<THandler, TArgs>))),
            _ => throw new InvalidOperationException("Unsupported RPC return kind."),
        };
    }

    private static Func<THandler, CancelToken, RpcVoid> CreateNoArgsVoidHandler<THandler>(MethodInfo method, bool hasToken, RpcReturnKind kind) =>
        (h, c) => CreateNoArgsVoidHandlerAsync<THandler>(method, hasToken, kind).Invoke(h, c).GetAwaiter().GetResult();

    private static Func<THandler, CancelToken, Promise<RpcVoid>> CreateNoArgsVoidHandlerAsync<THandler>(
        MethodInfo method,
        bool hasToken,
        RpcReturnKind kind)
    {
        if (hasToken)
        {
            return kind switch
            {
                RpcReturnKind.Promise => WrapVoid(
                    (Func<THandler, CancelToken, Promise>)method.CreateDelegate(typeof(Func<THandler, CancelToken, Promise>))),
                RpcReturnKind.Task => WrapVoid((Func<THandler, CancelToken, Task>)method.CreateDelegate(typeof(Func<THandler, CancelToken, Task>))),
                RpcReturnKind.ValueTask => WrapVoid(
                    (Func<THandler, CancelToken, ValueTask>)method.CreateDelegate(typeof(Func<THandler, CancelToken, ValueTask>))),
                RpcReturnKind.Sync => WrapVoid((Action<THandler, CancelToken>)method.CreateDelegate(typeof(Action<THandler, CancelToken>))),
                _ => throw new InvalidOperationException("Unsupported RPC return kind."),
            };
        }

        return kind switch
        {
            RpcReturnKind.Promise => WrapVoid((Func<THandler, Promise>)method.CreateDelegate(typeof(Func<THandler, Promise>))),
            RpcReturnKind.Task => WrapVoid((Func<THandler, Task>)method.CreateDelegate(typeof(Func<THandler, Task>))),
            RpcReturnKind.ValueTask => WrapVoid((Func<THandler, ValueTask>)method.CreateDelegate(typeof(Func<THandler, ValueTask>))),
            RpcReturnKind.Sync => WrapVoid((Action<THandler>)method.CreateDelegate(typeof(Action<THandler>))),
            _ => throw new InvalidOperationException("Unsupported RPC return kind."),
        };
    }

    private static Func<THandler, TArgs, CancelToken, Promise<TResult>> WrapNoToken<THandler, TArgs, TResult>(
        Func<THandler, TArgs, Promise<TResult>> handler) =>
        (target, args, _) => handler(target, args);

    private static Func<THandler, CancelToken, Promise<TResult>> WrapNoToken<THandler, TResult>(Func<THandler, Promise<TResult>> handler) =>
        (target, _) => handler(target);

    private static Func<THandler, TArgs, CancelToken, Promise<TResult>> WrapTask<THandler, TArgs, TResult>(
        Func<THandler, TArgs, CancelToken, Task<TResult>> handler) =>
        (target, args, token) => handler(target, args, token);

    private static Func<THandler, TArgs, CancelToken, Promise<TResult>> WrapTask<THandler, TArgs, TResult>(
        Func<THandler, TArgs, Task<TResult>> handler) =>
        (target, args, _) => handler(target, args);

    private static Func<THandler, CancelToken, Promise<TResult>> WrapTask<THandler, TResult>(Func<THandler, CancelToken, Task<TResult>> handler) =>
        (target, token) => handler(target, token);

    private static Func<THandler, CancelToken, Promise<TResult>> WrapTask<THandler, TResult>(Func<THandler, Task<TResult>> handler) =>
        (target, _) => handler(target);

    private static Func<THandler, TArgs, CancelToken, Promise<TResult>> WrapValueTask<THandler, TArgs, TResult>(
        Func<THandler, TArgs, CancelToken, ValueTask<TResult>> handler) =>
        (target, args, token) => handler(target, args, token);

    private static Func<THandler, TArgs, CancelToken, Promise<TResult>> WrapValueTask<THandler, TArgs, TResult>(
        Func<THandler, TArgs, ValueTask<TResult>> handler) =>
        (target, args, _) => handler(target, args);

    private static Func<THandler, CancelToken, Promise<TResult>> WrapValueTask<THandler, TResult>(
        Func<THandler, CancelToken, ValueTask<TResult>> handler) =>
        (target, token) => handler(target, token);

    private static Func<THandler, CancelToken, Promise<TResult>> WrapValueTask<THandler, TResult>(Func<THandler, ValueTask<TResult>> handler) =>
        (target, _) => handler(target);

    private static Func<THandler, TArgs, CancelToken, Promise<TResult>> WrapSync<THandler, TArgs, TResult>(
        Func<THandler, TArgs, CancelToken, TResult> handler) =>
        (target, args, token) => handler(target, args, token);

    private static Func<THandler, TArgs, CancelToken, Promise<TResult>> WrapSync<THandler, TArgs, TResult>(Func<THandler, TArgs, TResult> handler) =>
        (target, args, _) => handler(target, args);

    private static Func<THandler, CancelToken, Promise<TResult>> WrapSync<THandler, TResult>(Func<THandler, CancelToken, TResult> handler) =>
        (target, token) => handler(target, token);

    private static Func<THandler, CancelToken, Promise<TResult>> WrapSync<THandler, TResult>(Func<THandler, TResult> handler) =>
        (target, _) => handler(target);

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> WrapVoid<THandler, TArgs>(
        Func<THandler, TArgs, CancelToken, Promise> handler) =>
        (target, args, token) => AwaitVoid(handler(target, args, token));

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> WrapVoid<THandler, TArgs>(Func<THandler, TArgs, Promise> handler) =>
        (target, args, _) => AwaitVoid(handler(target, args));

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> WrapVoid<THandler, TArgs>(Func<THandler, TArgs, CancelToken, Task> handler) =>
        (target, args, token) => AwaitVoid(handler(target, args, token));

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> WrapVoid<THandler, TArgs>(Func<THandler, TArgs, Task> handler) =>
        (target, args, _) => AwaitVoid(handler(target, args));

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> WrapVoid<THandler, TArgs>(
        Func<THandler, TArgs, CancelToken, ValueTask> handler) =>
        (target, args, token) => AwaitVoid(handler(target, args, token));

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> WrapVoid<THandler, TArgs>(Func<THandler, TArgs, ValueTask> handler) =>
        (target, args, _) => AwaitVoid(handler(target, args));

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> WrapVoid<THandler, TArgs>(Action<THandler, TArgs, CancelToken> handler) =>
        (target, args, token) => InvokeVoid(handler, target, args, token);

    private static Func<THandler, TArgs, CancelToken, Promise<RpcVoid>> WrapVoid<THandler, TArgs>(Action<THandler, TArgs> handler) =>
        (target, args, _) => InvokeVoid(handler, target, args);

    private static Func<THandler, CancelToken, Promise<RpcVoid>> WrapVoid<THandler>(Func<THandler, CancelToken, Promise> handler) =>
        (target, token) => AwaitVoid(handler(target, token));

    private static Func<THandler, CancelToken, Promise<RpcVoid>> WrapVoid<THandler>(Func<THandler, Promise> handler) =>
        (target, _) => AwaitVoid(handler(target));

    private static Func<THandler, CancelToken, Promise<RpcVoid>> WrapVoid<THandler>(Func<THandler, CancelToken, Task> handler) =>
        (target, token) => AwaitVoid(handler(target, token));

    private static Func<THandler, CancelToken, Promise<RpcVoid>> WrapVoid<THandler>(Func<THandler, Task> handler) =>
        (target, _) => AwaitVoid(handler(target));

    private static Func<THandler, CancelToken, Promise<RpcVoid>> WrapVoid<THandler>(Func<THandler, CancelToken, ValueTask> handler) =>
        (target, token) => AwaitVoid(handler(target, token));

    private static Func<THandler, CancelToken, Promise<RpcVoid>> WrapVoid<THandler>(Func<THandler, ValueTask> handler) =>
        (target, _) => AwaitVoid(handler(target));

    private static Func<THandler, CancelToken, Promise<RpcVoid>> WrapVoid<THandler>(Action<THandler, CancelToken> handler) =>
        (target, token) => InvokeVoid(handler, target, token);

    private static Func<THandler, CancelToken, Promise<RpcVoid>> WrapVoid<THandler>(Action<THandler> handler) =>
        (target, _) => InvokeVoid(handler, target);

    private static async Promise<RpcVoid> AwaitVoid(Promise task)
    {
        await task;

        return default(RpcVoid);
    }

    private static Promise<RpcVoid> InvokeVoid<THandler>(Action<THandler> handler, THandler target)
    {
        handler(target);

        return default(RpcVoid);
    }

    private static Promise<RpcVoid> InvokeVoid<THandler>(Action<THandler, CancelToken> handler, THandler target, CancelToken cancelToken)
    {
        handler(target, cancelToken);

        return default(RpcVoid);
    }

    private static Promise<RpcVoid> InvokeVoid<THandler, TArgs>(Action<THandler, TArgs> handler, THandler target, TArgs args)
    {
        handler(target, args);

        return default(RpcVoid);
    }

    private static Promise<RpcVoid> InvokeVoid<THandler, TArgs>(
        Action<THandler, TArgs, CancelToken> handler,
        THandler target,
        TArgs args,
        CancelToken cancelToken)
    {
        handler(target, args, cancelToken);

        return default(RpcVoid);
    }

    private static RpcReturnInfo GetReturnInfo(Type returnType)
    {
        if (returnType == typeof(void))
            return RpcReturnInfo.Void(RpcReturnKind.Sync);

        if (returnType == typeof(Promise))
            return RpcReturnInfo.Void(RpcReturnKind.Promise);

        if (returnType == typeof(Task))
            return RpcReturnInfo.Void(RpcReturnKind.Task);

        if (returnType == typeof(ValueTask))
            return RpcReturnInfo.Void(RpcReturnKind.ValueTask);

        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            var resultType = returnType.GetGenericArguments()[0];

            if (definition == typeof(Promise<>))
                return RpcReturnInfo.Result(RpcReturnKind.Promise, resultType);

            if (definition == typeof(Task<>))
                return RpcReturnInfo.Result(RpcReturnKind.Task, resultType);

            if (definition == typeof(ValueTask<>))
                return RpcReturnInfo.Result(RpcReturnKind.ValueTask, resultType);
        }

        return RpcReturnInfo.Result(RpcReturnKind.Sync, returnType);
    }

    private enum RpcReturnKind
    {
        Promise,
        Task,
        ValueTask,
        Sync,
    }

    private readonly record struct RpcReturnInfo(RpcReturnKind Kind, Type ResultType, bool IsVoid)
    {
        public static RpcReturnInfo Void(RpcReturnKind kind) => new(kind, typeof(RpcVoid), true);

        public static RpcReturnInfo Result(RpcReturnKind kind, Type resultType) =>
            new(kind, resultType ?? throw new ArgumentNullException(nameof(resultType)), false);
    }

    private readonly struct RpcVoid { }

    private abstract class EmbeddedWebRpcMethod(string name, bool isVoid)
    {
        public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
        protected bool IsVoid { get; } = isVoid;
        public abstract NetHttpResponse Invoke(object handler, CharStreamBr request, CancelToken cancelToken);

        protected NetHttpResponse Ok(IStreamRs<byte> payload)
        {
            var headers = new NetHttpHeaders
            {
                ContentType = EmbeddedWebRpcJson.ContentType,
            };

            return new NetHttpResponse(NetHttpStatus.Ok, headers, payload);
        }

        protected static NetHttpResponse BadRequest() => new(NetHttpStatus.BadRequest);
    }

    private sealed class EmbeddedWebRpcArgsMethod<THandler, TArgs, TResult>(
        string name,
        Func<THandler, TArgs, CancelToken, TResult> handler,
        bool isVoid) : EmbeddedWebRpcMethod(name, isVoid)
    {
        private readonly Func<THandler, TArgs, CancelToken, TResult> handler = handler ?? throw new ArgumentNullException(nameof(handler));

        public override NetHttpResponse Invoke(object handler, CharStreamBr body, CancelToken cancelToken)
        {
            if (body.IsEmpty)
                return BadRequest();

            var args = EmbeddedWebRpcJson.Deserialize<TArgs>(body.ReadToEnd().Span);
            var result = this.handler((THandler)handler, args, cancelToken);

            if (this.IsVoid)
                return this.Ok(EmbeddedWebRpcJson.NullPayload);

            var payload = EmbeddedWebRpcJson.Serialize(result);

            return this.Ok(payload);
        }
    }

    private sealed class EmbeddedWebRpcNoArgsMethod<THandler, TResult>(string name, Func<THandler, CancelToken, TResult> handler, bool isVoid)
        : EmbeddedWebRpcMethod(name, isVoid)
    {
        private readonly Func<THandler, CancelToken, TResult> handler = handler ?? throw new ArgumentNullException(nameof(handler));

        public override NetHttpResponse Invoke(object handler, CharStreamBr body, CancelToken cancelToken)
        {
            if (body.IsEmpty)
                return BadRequest();

            var result = this.handler((THandler)handler, cancelToken);

            if (this.IsVoid)
                return this.Ok(EmbeddedWebRpcJson.NullPayload);

            var payload = EmbeddedWebRpcJson.Serialize(result);

            return this.Ok(payload);
        }
    }

    private static class EmbeddedWebRpcJson
    {
        public const string ContentType = "application/json; charset=utf-8";

        private static readonly JsonSerializerOptions options = CreateOptions();

        public static IStreamRs<byte> NullPayload => new StreamTrs<byte>("null"u8.ToArray());

        public static IStreamRs<byte> Serialize<T>(T value) =>
            new StreamTrs<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, options)));

        public static T Deserialize<T>(ReadOnlySpan<char> value) =>
            JsonSerializer.Deserialize<T>(value, options)!;

        private static JsonSerializerOptions CreateOptions()
        {
            var jsonOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            return jsonOptions;
        }
    }
}
