// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.IO;
using Itexoft.Net.Http;

namespace Itexoft.EmbeddedWeb;

internal static class EmbeddedWebRpcClientScript
{
    private const string scriptFileName = "client.js";
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    private static readonly ReadOnlyMemory<byte> scriptBytes = strictUtf8.GetBytes(BuildScript());

    public static bool IsScriptRequest(string path, string prefix)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        return path.Equals(GetScriptPath(prefix), StringComparison.OrdinalIgnoreCase);
    }

    public static NetHttpResponse CreateResponse(NetHttpMethod method)
    {
        if (!IsGetOrHead(method))
            return new NetHttpResponse(NetHttpStatus.MethodNotAllowed);

        var headers = new NetHttpHeaders
        {
            ContentType = "application/javascript; charset=utf-8",
        };

        return new NetHttpResponse(NetHttpStatus.Ok, headers, new StreamTrs<byte>(scriptBytes));
    }

    public static bool TryInject(ReadOnlyMemory<byte> html, string prefix, out ReadOnlyMemory<byte> injected)
    {
        injected = ReadOnlyMemory<byte>.Empty;

        if (html.Length == 0)
            return false;

        string text;

        try
        {
            text = strictUtf8.GetString(html.Span);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var tag = GetScriptTag(prefix);
        var index = text.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        if (index < 0)
            index = text.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);

        if (index < 0)
            return false;

        var builder = new StringBuilder(text.Length + tag.Length);
        builder.Append(text.AsSpan(0, index));
        builder.Append(tag);
        builder.Append(text.AsSpan(index));

        injected = strictUtf8.GetBytes(builder.ToString());

        return true;
    }

    public static string GetScriptPath(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("RPC path prefix cannot be empty.", nameof(prefix));

        if (prefix == "/")
            return "/" + scriptFileName;

        return prefix + "/" + scriptFileName;
    }

    private static string GetScriptTag(string prefix)
    {
        var path = GetScriptPath(prefix);

        return $"<script src=\"{path}\"></script>";
    }

    private static string BuildScript()
    {
        const string script = "(function(global){"
                              + "\"use strict\";"
                              + "if(!global){throw new Error(\"Global object is required.\");}"
                              + "function requireFetch(){if(typeof fetch!==\"function\"){throw new Error(\"fetch is required.\");}}"
                              + "function validatePrefix(prefix){"
                              + "if(!prefix){throw new Error(\"RPC base path is required.\");}"
                              + "if(prefix[0]!==\"/\"){throw new Error(\"RPC base path must start with '/'.\");}"
                              + "if(prefix.length>1&&prefix[prefix.length-1]===\"/\"){throw new Error(\"RPC base path must not end with '/'.\");}"
                              + "return prefix;"
                              + "}"
                              + "function validateMethod(method){"
                              + "if(!method){throw new Error(\"RPC method is required.\");}"
                              + "if(method.indexOf(\"/\")>=0){throw new Error(\"RPC method cannot contain '/'.\");}"
                              + "return method;"
                              + "}"
                              + "function RpcClient(basePath){this.basePath=validatePrefix(basePath||\"/rpc\");}"
                              + "RpcClient.prototype.call=function(method,args){"
                              + "requireFetch();"
                              + "method=validateMethod(method);"
                              + "var body=undefined;"
                              + "var headers={};"
                              + "if(typeof args!==\"undefined\"){"
                              + "body=JSON.stringify(args);"
                              + "headers[\"Content-Type\"]=\"application/json; charset=utf-8\";"
                              + "}"
                              + "return fetch(this.basePath+\"/\"+method,{method:\"POST\",headers:headers,body:body})"
                              + ".then(function(resp){"
                              + "if(!resp.ok){return resp.text().then(function(text){throw new Error(\"RPC \"+resp.status+\" \"+text);});}"
                              + "return resp.text();"
                              + "}).then(function(text){"
                              + "if(!text){return null;}"
                              + "return JSON.parse(text);"
                              + "});"
                              + "};"
                              + "RpcClient.prototype.api=function(){"
                              + "if(typeof Proxy!==\"function\"){throw new Error(\"Proxy is required.\");}"
                              + "var client=this;"
                              + "return new Proxy({}, {"
                              + "get:function(_,prop){"
                              + "if(prop===\"call\"){return client.call.bind(client);}"
                              + "if(typeof prop!==\"string\"){return undefined;}"
                              + "return function(args){return client.call(prop,args);};"
                              + "}"
                              + "});"
                              + "};"
                              + "function create(basePath){return new RpcClient(basePath);}"
                              + "function api(basePath){return create(basePath).api();}"
                              + "global.ItexoftRpc={Client:RpcClient,create:create,api:api};"
                              + "})(typeof globalThis===\"undefined\"?null:globalThis);";

        return script;
    }

    private static bool IsGetOrHead(NetHttpMethod method)
    {
        var value = method.Value.ToString();

        return value.Equals("GET", StringComparison.OrdinalIgnoreCase) || value.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
    }
}
