(function () {
    const protocol = Object.freeze({
        webSocketPath: '/ws',
        typeSnapshot: 'snapshot',
        typeChatAdded: 'chat-added',
        typeChatRemoved: 'chat-removed',
        typeChatStatus: 'chat-status',
        typeMessageAdded: 'message-added',
        typeMessageAppended: 'message-appended',
        typeMessageCompleted: 'message-completed',
        typeSend: 'send',
        statusLive: 'live',
        statusEnded: 'ended',
        statusError: 'error',
        directionIncoming: 'incoming',
        directionOutgoing: 'outgoing'
    });
    const ui = Object.freeze({
        reconnectDelayMs: 1000,
        scrollPinnedThresholdPx: 24
    });
    const connection = Object.freeze({
        connecting: 'connecting',
        live: 'live',
        reconnecting: 'reconnecting'
    });

    const elements = {
        chatList: document.getElementById('chat-list'),
        chatState: document.getElementById('chat-state'),
        chatSubtitle: document.getElementById('chat-subtitle'),
        chatTitle: document.getElementById('chat-title'),
        composer: document.getElementById('composer'),
        composerHint: document.getElementById('composer-hint'),
        composerInput: document.getElementById('composer-input'),
        composerSend: document.getElementById('composer-send'),
        connectionStatus: document.getElementById('connection-status'),
        messageList: document.getElementById('message-list')
    };

    const state = {
        activeKey: null,
        chats: new Map(),
        connectionMode: connection.connecting,
        reconnectTimer: 0,
        socket: null
    };

    elements.composer.addEventListener('submit', onComposerSubmit);
    elements.composerInput.addEventListener('keydown', onComposerKeyDown);

    connect();
    renderConnectionState();
    renderSidebar();
    renderActiveChat('bottom');
    renderComposerState();

    function connect() {
        clearReconnect();
        state.connectionMode = connection.connecting;
        renderConnectionState();
        renderComposerState();
        const url = new URL(protocol.webSocketPath, window.location.href);
        url.protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const socket = new WebSocket(url);
        state.socket = socket;

        socket.addEventListener('open', function () {
            if (state.socket !== socket) {
                socket.close();
                return;
            }

            state.connectionMode = connection.live;
            renderConnectionState();
            renderComposerState();
        });

        socket.addEventListener('message', function (event) {
            if (state.socket !== socket)
                return;

            handlePayload(JSON.parse(event.data));
        });

        socket.addEventListener('error', function () {
            if (state.socket === socket)
                socket.close();
        });

        socket.addEventListener('close', function () {
            if (state.socket !== socket)
                return;

            state.socket = null;
            state.connectionMode = connection.reconnecting;
            renderConnectionState();
            renderComposerState();
            state.reconnectTimer = window.setTimeout(connect, ui.reconnectDelayMs);
        });
    }

    function clearReconnect() {
        if (state.reconnectTimer === 0)
            return;

        window.clearTimeout(state.reconnectTimer);
        state.reconnectTimer = 0;
    }

    function handlePayload(payload) {
        switch (payload.type) {
            case protocol.typeSnapshot:
                replaceSnapshot(payload.chats);
                break;
            case protocol.typeChatAdded:
                applyChatAdded(payload);
                break;
            case protocol.typeChatRemoved:
                applyChatRemoved(payload.key);
                break;
            case protocol.typeChatStatus:
                applyChatStatus(payload.key, payload.status);
                break;
            case protocol.typeMessageAdded:
                applyMessageAdded(payload.key, payload.message);
                break;
            case protocol.typeMessageAppended:
                applyMessageAppended(payload.key, payload.messageId, payload.textFragment);
                break;
            case protocol.typeMessageCompleted:
                applyMessageCompleted(payload.key, payload.messageId);
                break;
            default:
                throw new Error('Unsupported payload type: ' + payload.type);
        }
    }

    function applyChatAdded(payload) {
        state.chats.set(payload.key, cloneChat(payload));
        const activeChanged = ensureActiveChat();
        renderSidebar();

        if (activeChanged)
            renderActiveChat('bottom');

        renderComposerState();
    }

    function applyChatRemoved(key) {
        if (!state.chats.delete(key))
            return;

        const activeChanged = state.activeKey === key;

        if (activeChanged)
            state.activeKey = null;

        ensureActiveChat();
        renderSidebar();

        if (activeChanged)
            renderActiveChat('bottom');

        renderComposerState();
    }

    function applyChatStatus(key, status) {
        const chat = state.chats.get(key);

        if (!chat)
            return;

        chat.status = status;
        renderSidebar();

        if (key === state.activeKey) {
            renderActiveChat('preserve');
            renderComposerState();
        }
    }

    function applyMessageAdded(key, messagePayload) {
        const chat = state.chats.get(key);

        if (!chat)
            return;

        chat.messages.push(cloneMessage(messagePayload));
        renderSidebar();

        if (key === state.activeKey)
            renderActiveChat('preserve');
    }

    function applyMessageAppended(key, messageId, textFragment) {
        const chat = state.chats.get(key);
        const message = chat ? findMessage(chat, messageId) : null;

        if (!message)
            return;

        message.text += textFragment;
        renderSidebar();

        if (key === state.activeKey)
            renderActiveChat('preserve');
    }

    function applyMessageCompleted(key, messageId) {
        const chat = state.chats.get(key);
        const message = chat ? findMessage(chat, messageId) : null;

        if (!message)
            return;

        message.completed = true;
        renderSidebar();

        if (key === state.activeKey)
            renderActiveChat('preserve');
    }

    function cloneChat(chat) {
        return {
            key: chat.key,
            order: chat.order,
            status: chat.status,
            messages: Array.isArray(chat.messages) ? chat.messages.map(cloneMessage) : []
        };
    }

    function cloneMessage(message) {
        return {
            id: message.id,
            direction: message.direction,
            text: message.text,
            timestampUtcMs: message.timestampUtcMs,
            completed: message.completed
        };
    }

    function ensureActiveChat() {
        if (state.activeKey && state.chats.has(state.activeKey))
            return false;

        const nextKey = orderedChats()[0]?.key ?? null;
        const changed = state.activeKey !== nextKey;
        state.activeKey = nextKey;

        return changed;
    }

    function findMessage(chat, messageId) {
        for (let i = 0; i < chat.messages.length; i++) {
            if (chat.messages[i].id === messageId)
                return chat.messages[i];
        }

        return null;
    }

    function onComposerSubmit(event) {
        event.preventDefault();

        const chat = currentChat();
        const value = elements.composerInput.value;

        if (!chat || chat.status !== protocol.statusLive || !value.trim() || !state.socket || state.socket.readyState !== WebSocket.OPEN)
            return;

        state.socket.send(JSON.stringify({
            type: protocol.typeSend,
            key: chat.key,
            text: value
        }));
        elements.composerInput.value = '';
        renderComposerState();
    }

    function onComposerKeyDown(event) {
        if (event.key !== 'Enter' || event.shiftKey || event.isComposing)
            return;

        event.preventDefault();
        elements.composer.requestSubmit();
    }

    function orderedChats() {
        return Array.from(state.chats.values()).sort(function (left, right) {
            return left.order - right.order;
        });
    }

    function renderSidebar() {
        const chats = orderedChats();
        elements.chatList.textContent = '';

        if (chats.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'chat-list-empty';
            empty.textContent = 'No chats are connected yet.';
            elements.chatList.append(empty);
            return;
        }

        for (let i = 0; i < chats.length; i++) {
            const chat = chats[i];
            const item = document.createElement('button');
            const preview = chat.messages.length === 0 ? 'No messages yet.' : lastMessagePreview(chat.messages[chat.messages.length - 1]);
            item.type = 'button';
            item.className = 'chat-item' + (chat.key === state.activeKey ? ' is-active' : '');
            item.addEventListener('click', function () {
                if (state.activeKey === chat.key)
                    return;

                state.activeKey = chat.key;
                renderSidebar();
                renderActiveChat('bottom');
                renderComposerState();
            });
            item.innerHTML = '' +
                '<div class="chat-item-head">' +
                '<div class="chat-item-title"></div>' +
                '<div class="chat-item-status"></div>' +
                '</div>' +
                '<div class="chat-item-preview"></div>';
            item.querySelector('.chat-item-title').textContent = chat.key;
            const status = item.querySelector('.chat-item-status');
            status.textContent = chat.status;
            status.className = 'chat-item-status is-' + chat.status;
            item.querySelector('.chat-item-preview').textContent = preview;
            elements.chatList.append(item);
        }
    }

    function captureMessageViewport() {
        const bottomGap = Math.max(0, elements.messageList.scrollHeight - elements.messageList.scrollTop - elements.messageList.clientHeight);

        return {
            pinned: bottomGap <= ui.scrollPinnedThresholdPx,
            scrollTop: elements.messageList.scrollTop
        };
    }

    function renderActiveChat(scrollMode) {
        const viewport = scrollMode === 'preserve' ? captureMessageViewport() : null;
        const chat = currentChat();
        const messages = chat ? chat.messages.slice().sort(function (left, right) {
            if (left.timestampUtcMs !== right.timestampUtcMs)
                return left.timestampUtcMs - right.timestampUtcMs;

            return left.id - right.id;
        }) : [];
        elements.messageList.textContent = '';

        if (!chat) {
            elements.chatTitle.textContent = 'No active chat';
            elements.chatSubtitle.textContent = 'Add external streams to populate the list.';
            setChatState('idle', '');
            const empty = document.createElement('div');
            empty.className = 'message-list-empty';
            empty.textContent = 'Select a chat from the list to see the live conversation.';
            elements.messageList.append(empty);
            return;
        }

        elements.chatTitle.textContent = chat.key;
        elements.chatSubtitle.textContent = subtitleForChat(chat);
        setChatState(chat.status, 'is-' + chat.status);

        if (messages.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'message-list-empty';
            empty.textContent = 'No messages yet for this stream.';
            elements.messageList.append(empty);
            return;
        }

        for (let i = 0; i < messages.length; i++)
            elements.messageList.append(renderMessage(messages[i]));

        restoreMessageViewport(scrollMode, viewport);
    }

    function renderMessage(message) {
        const node = document.createElement('article');
        const directionLabel = message.direction === protocol.directionOutgoing ? 'You' : 'Stream';
        const timeLabel = new Date(message.timestampUtcMs).toLocaleString();
        node.className = 'message is-' + message.direction;
        node.innerHTML = '' +
            '<div class="message-meta">' +
            '<span></span>' +
            '<span></span>' +
            '</div>' +
            '<div class="message-body"></div>';
        const meta = node.querySelectorAll('.message-meta span');
        meta[0].textContent = directionLabel;
        meta[1].textContent = timeLabel;
        const body = node.querySelector('.message-body');
        body.textContent = message.text;

        if (!message.completed)
            body.classList.add('is-open');

        return node;
    }

    function replaceSnapshot(chats) {
        const previousActiveKey = state.activeKey;
        state.chats.clear();

        for (let i = 0; i < chats.length; i++) {
            const chat = cloneChat(chats[i]);
            state.chats.set(chat.key, chat);
        }

        ensureActiveChat();
        renderSidebar();
        renderActiveChat(state.activeKey === previousActiveKey ? 'preserve' : 'bottom');
        renderComposerState();
    }

    function renderConnectionState() {
        switch (state.connectionMode) {
            case connection.live:
                setConnectionStatus('Live', 'is-live');
                break;
            case connection.reconnecting:
                setConnectionStatus('Reconnecting', 'is-reconnecting');
                break;
            default:
                setConnectionStatus('Connecting', 'is-connecting');
                break;
        }
    }

    function restoreMessageViewport(scrollMode, viewport) {
        if (scrollMode === 'bottom' || !viewport || viewport.pinned) {
            elements.messageList.scrollTop = elements.messageList.scrollHeight;
            return;
        }

        elements.messageList.scrollTop = viewport.scrollTop;
    }

    function setChatState(text, className) {
        elements.chatState.textContent = text;
        elements.chatState.className = 'chat-state-badge' + (className ? ' ' + className : '');
    }

    function setConnectionStatus(text, className) {
        elements.connectionStatus.textContent = text;
        elements.connectionStatus.className = 'connection-status ' + className;
    }

    function currentChat() {
        return state.activeKey ? state.chats.get(state.activeKey) ?? null : null;
    }

    function lastMessagePreview(message) {
        const prefix = message.direction === protocol.directionOutgoing ? 'You: ' : 'Stream: ';
        const text = message.text || (message.completed ? '' : '...');
        return prefix + text;
    }

    function subtitleForChat(chat) {
        switch (chat.status) {
            case protocol.statusLive:
                return 'Live duplex stream. Incoming text is rendered progressively.';
            case protocol.statusEnded:
                return 'The external stream reached the end. History is preserved.';
            case protocol.statusError:
                return 'The external stream failed. History is preserved, sending is disabled.';
            default:
                return 'Unknown chat state.';
        }
    }

    function renderComposerState() {
        const chat = currentChat();
        const live = !!chat && chat.status === protocol.statusLive;
        const online = !!state.socket && state.socket.readyState === WebSocket.OPEN;
        const enabled = live && online;
        elements.composerInput.disabled = !enabled;
        elements.composerSend.disabled = !enabled;

        if (!chat)
            elements.composerHint.textContent = 'Select a chat to send a message.';
        else if (!online)
            elements.composerHint.textContent = 'WebSocket is reconnecting.';
        else if (chat.status !== protocol.statusLive)
            elements.composerHint.textContent = 'This chat is not writable anymore.';
        else
            elements.composerHint.textContent = 'Press Enter to send. Press Shift+Enter for a new line.';
    }
})();
