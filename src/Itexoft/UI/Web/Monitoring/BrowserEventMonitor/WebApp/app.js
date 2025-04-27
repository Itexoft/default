import {BrowserEventMonitorChart} from './chart.js';

const pollIntervalMs = 1000;
const forceResetSince = Number.MAX_SAFE_INTEGER;

class ContractViolation extends Error {
}

class TransportFailure extends Error {
}

const elements = {
    tabStrip: document.getElementById('tab-strip'),
    emptyState: document.getElementById('empty-state'),
    chartShell: document.getElementById('chart-shell'),
    baseCanvas: document.getElementById('chart-base'),
    overlayCanvas: document.getElementById('chart-overlay'),
    tooltip: document.getElementById('tooltip'),
    message: document.getElementById('chart-message'),
    statusBadge: document.getElementById('status-badge'),
    logToggle: document.getElementById('log-toggle'),
    logInput: document.getElementById('log-input'),
    toast: document.getElementById('toast'),
    deletePopover: document.getElementById('delete-popover'),
    deletePopoverText: document.getElementById('delete-popover-text'),
    deleteConfirm: document.getElementById('delete-confirm'),
    deleteCancel: document.getElementById('delete-cancel'),
};

const tabNodes = new Map();
const chart = new BrowserEventMonitorChart(
    {
        shell: elements.chartShell,
        baseCanvas: elements.baseCanvas,
        overlayCanvas: elements.overlayCanvas,
        tooltip: elements.tooltip,
        message: elements.message,
        statusBadge: elements.statusBadge,
        logToggle: elements.logToggle,
        logInput: elements.logInput,
    },
    {
        getState: () => currentState,
        getActiveCategory: () => currentState.activeKey ? currentState.categoriesByKey[currentState.activeKey] ?? null : null,
        patchCategory,
        showToast,
    });

let currentState = createInitialState();
let queuedState = null;
let commitHandle = 0;
let toastHandle = 0;
let deletePopoverKey = null;
let deleteAnchorKey = null;
let syncScheduled = false;
let syncInFlight = false;
let pendingRealtimeRevision = null;
let forceFullResync = true;
let pendingRevisionHint = 0;
let retryTransportFailure = false;
let transportMode = 'poll';
let eventSource = null;
let eventSourceOpen = false;
let eventSourceGeneration = 0;
let pollHandle = window.setInterval(() => {
    if (!eventSourceOpen)
        requestSync(currentState.globalRevision, 'poll');
}, pollIntervalMs);

elements.deleteConfirm.addEventListener('click', () => confirmDelete());
elements.deleteCancel.addEventListener('click', () => closeDeletePopover());
window.addEventListener('resize', () => positionDeletePopover());
window.addEventListener('scroll', () => positionDeletePopover(), true);
document.addEventListener('click', event => {
    if (elements.deletePopover.hidden)
        return;

    if (elements.deletePopover.contains(event.target))
        return;

    const anchor = deleteAnchorKey ? tabNodes.get(deleteAnchorKey)?.closeButton : null;
    if (anchor && anchor.contains(event.target))
        return;

    closeDeletePopover();
});

renderState();
startRealtime();
requestSync(0, 'boot');

function createInitialState() {
    return {
        serverInstanceId: null,
        globalRevision: 0,
        orderedKeys: [],
        activeKey: null,
        categoriesByKey: Object.create(null),
        pendingDeleteKey: null,
        statusText: 'Starting',
        statusClass: '',
    };
}

function getWorkingState() {
    return queuedState ?? currentState;
}

function cloneState(state) {
    return {
        ...state,
        orderedKeys: [...state.orderedKeys],
        categoriesByKey: {...state.categoriesByKey},
    };
}

function cloneCategory(category) {
    return {
        ...category,
        metadata: {...category.metadata},
        events: [...category.events],
        xView: {...category.xView},
        yView: {...category.yView},
    };
}

function queueCommit(nextState) {
    queuedState = nextState;
    if (commitHandle !== 0)
        return;

    commitHandle = window.requestAnimationFrame(() => {
        commitHandle = 0;
        if (!queuedState)
            return;

        currentState = queuedState;
        queuedState = null;
        renderState();
        if (pendingRealtimeRevision !== null) {
            const revision = pendingRealtimeRevision;
            pendingRealtimeRevision = null;
            restartRealtime(revision);
        }
    });
}

function renderState() {
    renderTabs();
    setElementHidden(elements.emptyState, currentState.orderedKeys.length > 0, 'grid');
    setElementHidden(elements.chartShell, currentState.orderedKeys.length === 0, 'block');
    chart.requestRender(true);
    positionDeletePopover();
}

function renderTabs() {
    const wanted = new Set(currentState.orderedKeys);
    for (const [key, node] of tabNodes) {
        if (wanted.has(key))
            continue;

        node.root.remove();
        tabNodes.delete(key);
    }

    for (const key of currentState.orderedKeys) {
        let node = tabNodes.get(key);
        if (!node) {
            node = createTabNode(key);
            tabNodes.set(key, node);
        }

        const active = currentState.activeKey === key;
        const pending = currentState.pendingDeleteKey === key;
        node.root.className = `monitor-tab${active ? ' is-active' : ''}${pending ? ' is-pending' : ''}`;
        node.label.textContent = key;
        node.selectButton.title = key;
        node.root.dataset.key = key;
        elements.tabStrip.append(node.root);
    }
}

function createTabNode(key) {
    const root = document.createElement('div');
    const selectButton = document.createElement('button');
    const closeButton = document.createElement('button');
    const label = document.createElement('span');

    root.className = 'monitor-tab';
    selectButton.className = 'monitor-tab-button';
    closeButton.className = 'monitor-tab-close';
    label.className = 'monitor-tab-label';
    closeButton.type = 'button';
    selectButton.type = 'button';
    closeButton.textContent = '×';
    closeButton.title = 'Delete category';
    selectButton.append(label);

    selectButton.addEventListener('click', () => {
        const targetKey = root.dataset.key;
        if (!targetKey)
            return;

        if (currentState.activeKey === targetKey)
            resetCategoryViewport(targetKey);
        else
            setActiveKey(targetKey);
    });

    closeButton.addEventListener('click', event => {
        event.stopPropagation();
        const targetKey = root.dataset.key;
        if (!targetKey)
            return;

        openDeletePopover(targetKey);
    });

    root.append(selectButton, closeButton);
    return {root, selectButton, closeButton, label};
}

function setActiveKey(key) {
    const base = cloneState(getWorkingState());
    if (!(key in base.categoriesByKey))
        return;

    base.activeKey = key;
    queueCommit(base);
}

function patchCategory(key, updater) {
    const base = cloneState(getWorkingState());
    const category = base.categoriesByKey[key];
    if (!category)
        return;

    base.categoriesByKey[key] = updater(cloneCategory(category));
    queueCommit(base);
}

function resetCategoryViewport(key) {
    patchCategory(key, category => ({
        ...category,
        xView: {...category.xView, mode: 'live'},
        yView: {...category.yView, mode: 'autoFitVisible'},
    }));
}

function openDeletePopover(key) {
    deletePopoverKey = key;
    deleteAnchorKey = key;
    elements.deletePopoverText.textContent = `Delete "${key}" on the server and remove the tab.`;
    setElementHidden(elements.deletePopover, false, 'block');
    positionDeletePopover();
}

function closeDeletePopover() {
    deletePopoverKey = null;
    deleteAnchorKey = null;
    setElementHidden(elements.deletePopover, true, 'block');
}

function positionDeletePopover() {
    if (elements.deletePopover.hidden || !deleteAnchorKey)
        return;

    const anchor = tabNodes.get(deleteAnchorKey)?.closeButton;
    if (!anchor) {
        closeDeletePopover();

        return;
    }

    const shellRect = elements.chartShell.parentElement.getBoundingClientRect();
    const anchorRect = anchor.getBoundingClientRect();
    elements.deletePopover.style.left = `${Math.max(12, anchorRect.left - shellRect.left - 140)}px`;
    elements.deletePopover.style.top = `${Math.max(12, anchorRect.bottom - shellRect.top + 10)}px`;
}

async function confirmDelete() {
    const key = deletePopoverKey;
    if (!key)
        return;

    closeDeletePopover();
    const pending = cloneState(getWorkingState());
    pending.pendingDeleteKey = key;
    queueCommit(pending);

    try {
        const response = await requestJson(`/api/series/${encodeURIComponent(key)}`, {method: 'DELETE'});
        if (response.status !== 200 || !isNonNegativeInteger(response.payload?.globalRevision))
            throw new ContractViolation('Delete response is invalid.');

        const next = cloneState(getWorkingState());
        next.pendingDeleteKey = null;
        next.globalRevision = Math.max(next.globalRevision, response.payload.globalRevision);
        removeCategory(next, key, currentState);
        queueCommit(next);
        requestSync(response.payload.globalRevision, 'delete');
    } catch (error) {
        const next = cloneState(getWorkingState());
        next.pendingDeleteKey = null;
        queueCommit(next);
        showToast(error instanceof Error ? error.message : 'Delete failed.');
    }
}

function removeCategory(state, key, previousState) {
    delete state.categoriesByKey[key];
    state.orderedKeys = state.orderedKeys.filter(value => value !== key);
    if (state.activeKey === key)
        state.activeKey = chooseActiveKey(previousState, state);
}

function chooseActiveKey(previousState, nextState) {
    if (nextState.orderedKeys.length === 0)
        return null;

    if (previousState.activeKey && nextState.categoriesByKey[previousState.activeKey])
        return previousState.activeKey;

    const previousIndex = previousState.orderedKeys.indexOf(previousState.activeKey);
    for (let offset = 1; offset <= previousState.orderedKeys.length; offset += 1) {
        const left = previousState.orderedKeys[previousIndex - offset];
        if (left && nextState.categoriesByKey[left])
            return left;
    }

    for (let offset = 1; offset <= previousState.orderedKeys.length; offset += 1) {
        const right = previousState.orderedKeys[previousIndex + offset];
        if (right && nextState.categoriesByKey[right])
            return right;
    }

    return nextState.orderedKeys[0];
}

function showToast(message) {
    window.clearTimeout(toastHandle);
    elements.toast.textContent = message;
    setElementHidden(elements.toast, false, 'block');
    toastHandle = window.setTimeout(() => setElementHidden(elements.toast, true, 'block'), 2600);
}

function startRealtime() {
    if (!('EventSource' in window)) {
        transportMode = 'poll';
        setStatus('Live · Poll', 'is-live');

        return;
    }

    restartRealtime(currentState.globalRevision);
}

function restartRealtime(sinceRevision = currentState.globalRevision) {
    if (eventSource)
        eventSource.close();

    const generation = ++eventSourceGeneration;
    eventSourceOpen = false;
    const source = new EventSource(`/api/events?since=${encodeURIComponent(String(sinceRevision))}`);
    eventSource = source;
    source.addEventListener('open', () => {
        if (eventSource !== source || eventSourceGeneration !== generation)
            return;

        eventSourceOpen = true;
        transportMode = 'sse';
        if (retryTransportFailure) {
            requestSync(currentState.globalRevision, 'reopen');
            return;
        }
        if (!syncInFlight)
            setStatus('Live · SSE', 'is-live');
    });
    source.addEventListener('revision', event => {
        if (eventSource !== source || eventSourceGeneration !== generation)
            return;

        eventSourceOpen = true;
        transportMode = 'sse';
        try {
            const payload = JSON.parse(event.data);
            if (isNonNegativeInteger(payload?.globalRevision))
                requestSync(payload.globalRevision, 'event');
        } catch {
            forceFullResync = true;
            setStatus('Degraded', 'is-degraded');
        }
    });
    source.addEventListener('error', () => {
        if (eventSource !== source || eventSourceGeneration !== generation)
            return;

        eventSourceOpen = false;
        transportMode = 'poll';
        if (!syncInFlight)
            setStatus(retryTransportFailure ? 'Offline' : 'Live · Poll', retryTransportFailure ? 'is-offline' : 'is-live');
    });
}

function requestSync(revisionHint, source = 'event') {
    if (retryTransportFailure && source !== 'boot' && source !== 'poll' && source !== 'delete' && source !== 'reopen')
        return;
    pendingRevisionHint = Math.max(pendingRevisionHint, revisionHint);
    if (syncInFlight || syncScheduled)
        return;

    syncScheduled = true;
    queueMicrotask(performSync);
}

async function performSync() {
    if (syncInFlight)
        return;

    syncScheduled = false;
    syncInFlight = true;
    setStatus('Syncing', 'is-live');
    const base = currentState;
    const requestReset = forceFullResync || !base.serverInstanceId;
    let committedRevision = null;

    try {
        const modelResponse = await requestJson(`/api/model?since=${requestReset ? forceResetSince : base.globalRevision}`);
        if (modelResponse.status !== 200)
            throw new TransportFailure(`Model request failed with HTTP ${modelResponse.status}.`);

        const model = normalizeModel(modelResponse.payload, base, requestReset);
        const next = requestReset || model.mode === 'reset' ? createInitialState() : cloneState(base);
        next.serverInstanceId = model.serverInstanceId;
        next.globalRevision = model.globalRevision;
        next.pendingDeleteKey = base.pendingDeleteKey;
        const changedKeys = [];

        if (requestReset || model.mode === 'reset') {
            next.orderedKeys = [];
            next.categoriesByKey = Object.create(null);
            for (const metadata of model.categories) {
                next.orderedKeys.push(metadata.key);
                changedKeys.push(metadata.key);
            }
        } else {
            for (const key of model.deletedKeys)
                removeCategory(next, key, base);
            for (const metadata of model.categories) {
                if (!(metadata.key in next.categoriesByKey))
                    next.orderedKeys.push(metadata.key);
                changedKeys.push(metadata.key);
            }
        }

        const metadataByKey = Object.fromEntries(model.categories.map(metadata => [metadata.key, metadata]));
        const bodies = await Promise.all(changedKeys.map(key => loadSeriesBody(key, metadataByKey[key], base.categoriesByKey[key] ?? null)));

        for (const body of bodies) {
            if (body.kind === 'absent') {
                removeCategory(next, body.key, base);
                continue;
            }

            next.categoriesByKey[body.category.key] = body.category;
            if (body.logInvalidated)
                showToast(`"${body.category.key}" returned to linear scale because non-positive values arrived.`);
        }

        next.pendingDeleteKey = next.pendingDeleteKey && next.categoriesByKey[next.pendingDeleteKey] ? next.pendingDeleteKey : null;
        next.activeKey = chooseActiveKey(base, next);
        applyLiveStatus(next);
        queueCommit(next);
        committedRevision = next.globalRevision;
        forceFullResync = false;
        retryTransportFailure = false;

        if (requestReset || base.serverInstanceId !== next.serverInstanceId) {
            pendingRevisionHint = 0;
            pendingRealtimeRevision = next.globalRevision;
        }
    } catch (error) {
        forceFullResync = true;
        if (error instanceof TransportFailure) {
            retryTransportFailure = true;
            pendingRevisionHint = 0;
            setStatus('Offline', 'is-offline');
        } else
            setStatus('Degraded', 'is-degraded');
    } finally {
        syncInFlight = false;
        if (committedRevision !== null && pendingRevisionHint <= committedRevision)
            pendingRevisionHint = 0;
        if (committedRevision !== null && pendingRevisionHint > committedRevision && committedRevision > base.globalRevision)
            requestSync(pendingRevisionHint, 'followup');
    }
}

async function loadSeriesBody(key, metadata, previousCategory) {
    const knownRevision = previousCategory?.seriesRevision ?? 0;
    const knownCount = previousCategory?.events.length ?? 0;
    const response = await requestJson(`/api/series/${encodeURIComponent(key)}?knownRevision=${knownRevision}&knownCount=${knownCount}`);
    return normalizeSeriesResponse(response, metadata, previousCategory, knownRevision, knownCount);
}

function normalizeModel(payload, previousState, requestReset) {
    if (!payload || typeof payload !== 'object')
        throw new ContractViolation('Model payload must be an object.');

    const serverInstanceId = requiredText(payload.serverInstanceId, 'serverInstanceId');
    const globalRevision = requiredNonNegativeInteger(payload.globalRevision, 'globalRevision');
    const mode = payload.mode;
    if (mode !== 'delta' && mode !== 'reset')
        throw new ContractViolation('Model mode is invalid.');

    if (!Array.isArray(payload.categories) || !Array.isArray(payload.deletedKeys))
        throw new ContractViolation('Model arrays are invalid.');

    if (!requestReset && previousState.serverInstanceId && previousState.serverInstanceId !== serverInstanceId && mode !== 'reset')
        throw new ContractViolation('Server instance changed without reset.');

    if (!requestReset && previousState.serverInstanceId === serverInstanceId && globalRevision < previousState.globalRevision)
        throw new ContractViolation('Global revision moved backward.');

    const keys = new Set();
    const categories = payload.categories.map(item => {
        const metadata = normalizeMetadata(item);
        if (keys.has(metadata.key))
            throw new ContractViolation(`Duplicate key "${metadata.key}" in model payload.`);
        keys.add(metadata.key);
        return metadata;
    });

    const deletedKeys = payload.deletedKeys.map(item => {
        const value = requiredText(item, 'deletedKeys[]');
        if (keys.has(value))
            throw new ContractViolation(`Deleted key "${value}" also exists in categories.`);
        keys.add(value);
        return value;
    });

    return {serverInstanceId, globalRevision, mode, categories, deletedKeys};
}

function normalizeSeriesResponse(response, metadata, previousCategory, knownRevision, knownCount) {
    const payload = response.payload;
    if (response.status === 404) {
        if (!payload || payload.mode !== 'absent' || payload.key !== metadata.key)
            throw new ContractViolation(`Category "${metadata.key}" absence payload is invalid.`);

        return {kind: 'absent', key: metadata.key};
    }

    if (response.status !== 200)
        throw new TransportFailure(`Series request for "${metadata.key}" failed with HTTP ${response.status}.`);

    if (!payload || typeof payload !== 'object')
        throw new ContractViolation(`Series payload for "${metadata.key}" is invalid.`);

    if (payload.key !== metadata.key)
        throw new ContractViolation(`Series key mismatch for "${metadata.key}".`);

    const mode = payload.mode;
    if (mode !== 'append' && mode !== 'replace')
        throw new ContractViolation(`Series mode for "${metadata.key}" is invalid.`);

    const seriesRevision = requiredNonNegativeInteger(payload.seriesRevision, 'seriesRevision');
    const count = requiredNonNegativeInteger(payload.count, 'count');
    if (!Array.isArray(payload.events))
        throw new ContractViolation(`Series events for "${metadata.key}" must be an array.`);

    const events = payload.events.map(normalizeEvent);
    validateCanonicalOrder(events, metadata.key);

    let canonicalEvents;
    if (mode === 'append') {
        if (!previousCategory)
            throw new ContractViolation(`Append payload for "${metadata.key}" has no client prefix.`);
        if (seriesRevision < knownRevision)
            throw new ContractViolation(`Series revision for "${metadata.key}" moved backward.`);
        if (count !== knownCount + events.length)
            throw new ContractViolation(`Append count for "${metadata.key}" is inconsistent.`);
        if (events.length > 0 && previousCategory.events.length > 0 && events[0].timestampUtcMs < previousCategory.events[previousCategory.events.length - 1].timestampUtcMs)
            throw new ContractViolation(`Append payload for "${metadata.key}" is not a valid tail.`);
        canonicalEvents = previousCategory.events.concat(events);
    } else {
        if (count !== events.length)
            throw new ContractViolation(`Replace count for "${metadata.key}" is inconsistent.`);
        canonicalEvents = events;
    }

    if (seriesRevision !== metadata.seriesRevision || count !== metadata.count)
        throw new ContractViolation(`Series metadata for "${metadata.key}" contradicts the catalog.`);

    const derived = deriveMetadata(canonicalEvents);
    if (!metadataMatches(metadata, derived))
        throw new ContractViolation(`Series metadata for "${metadata.key}" does not match the canonical events.`);

    const previous = previousCategory;
    const logInvalidated = !!previous?.logYEnabled && !metadata.allValuesPositive;

    return {
        kind: 'series',
        category: {
            key: metadata.key,
            seriesRevision,
            count,
            metadata,
            events: canonicalEvents,
            loadState: 'loaded',
            xView: previous ? {...previous.xView} : {
                mode: 'live',
                minUtcMs: metadata.timeMinUtcMs,
                maxUtcMs: metadata.timeMaxUtcMs
            },
            yView: previous ? {...previous.yView} : {
                mode: 'autoFitVisible',
                minValue: metadata.valueMin,
                maxValue: metadata.valueMax
            },
            logYEnabled: logInvalidated ? false : !!previous?.logYEnabled,
        },
        logInvalidated,
    };
}

function normalizeMetadata(payload) {
    return {
        key: requiredText(payload.key, 'key'),
        seriesRevision: requiredNonNegativeInteger(payload.seriesRevision, 'seriesRevision'),
        count: requiredNonNegativeInteger(payload.count, 'count'),
        timeMinUtcMs: requiredInteger(payload.timeMinUtcMs, 'timeMinUtcMs'),
        timeMaxUtcMs: requiredInteger(payload.timeMaxUtcMs, 'timeMaxUtcMs'),
        valueMin: requiredFiniteNumber(payload.valueMin, 'valueMin'),
        valueMax: requiredFiniteNumber(payload.valueMax, 'valueMax'),
        hasText: requiredBoolean(payload.hasText, 'hasText'),
        allValuesPositive: requiredBoolean(payload.allValuesPositive, 'allValuesPositive'),
    };
}

function normalizeEvent(payload) {
    if (!payload || typeof payload !== 'object')
        throw new ContractViolation('Event payload must be an object.');

    const text = payload.text;
    if (text !== null && typeof text !== 'string')
        throw new ContractViolation('Event text must be string or null.');

    return {
        timestampUtcMs: requiredInteger(payload.timestampUtcMs, 'timestampUtcMs'),
        value: requiredFiniteNumber(payload.value, 'value'),
        text,
    };
}

function deriveMetadata(events) {
    if (events.length === 0) {
        return {
            count: 0,
            timeMinUtcMs: 0,
            timeMaxUtcMs: 0,
            valueMin: 0,
            valueMax: 0,
            hasText: false,
            allValuesPositive: true,
        };
    }

    let timeMinUtcMs = events[0].timestampUtcMs;
    let timeMaxUtcMs = events[0].timestampUtcMs;
    let valueMin = events[0].value;
    let valueMax = events[0].value;
    let hasText = events[0].text !== null;
    let allValuesPositive = events[0].value > 0;

    for (let index = 1; index < events.length; index += 1) {
        const event = events[index];
        if (event.timestampUtcMs < timeMinUtcMs)
            timeMinUtcMs = event.timestampUtcMs;
        if (event.timestampUtcMs > timeMaxUtcMs)
            timeMaxUtcMs = event.timestampUtcMs;
        if (event.value < valueMin)
            valueMin = event.value;
        if (event.value > valueMax)
            valueMax = event.value;
        hasText ||= event.text !== null;
        allValuesPositive &&= event.value > 0;
    }

    return {count: events.length, timeMinUtcMs, timeMaxUtcMs, valueMin, valueMax, hasText, allValuesPositive};
}

function validateCanonicalOrder(events, key) {
    for (let index = 1; index < events.length; index += 1) {
        if (events[index].timestampUtcMs < events[index - 1].timestampUtcMs)
            throw new ContractViolation(`Events for "${key}" are not in canonical order.`);
    }
}

function metadataMatches(metadata, derived) {
    return metadata.count === derived.count
        && metadata.timeMinUtcMs === derived.timeMinUtcMs
        && metadata.timeMaxUtcMs === derived.timeMaxUtcMs
        && metadata.valueMin === derived.valueMin
        && metadata.valueMax === derived.valueMax
        && metadata.hasText === derived.hasText
        && metadata.allValuesPositive === derived.allValuesPositive;
}

async function requestJson(url, init = undefined) {
    let response;
    try {
        response = await fetch(url, {
            cache: 'no-store',
            headers: {Accept: 'application/json'},
            ...init,
        });
    } catch (error) {
        throw new TransportFailure(error instanceof Error ? error.message : 'Network request failed.');
    }

    const text = await response.text();
    let payload = null;
    if (text.length > 0) {
        try {
            payload = JSON.parse(text);
        } catch {
            throw new ContractViolation(`Response from "${url}" is not valid JSON.`);
        }
    }

    return {status: response.status, payload};
}

function applyLiveStatus(state) {
    if (transportMode === 'sse' && eventSourceOpen) {
        state.statusText = 'Live · SSE';
        state.statusClass = 'is-live';
        return;
    }

    state.statusText = 'Live · Poll';
    state.statusClass = 'is-live';
}

function setStatus(text, className) {
    const next = cloneState(getWorkingState());
    next.statusText = text;
    next.statusClass = className;
    queueCommit(next);
}

function requiredText(value, name) {
    if (typeof value !== 'string' || value.trim().length === 0)
        throw new ContractViolation(`${name} must be a non-empty string.`);

    return value;
}

function requiredBoolean(value, name) {
    if (typeof value !== 'boolean')
        throw new ContractViolation(`${name} must be boolean.`);

    return value;
}

function requiredInteger(value, name) {
    if (!Number.isInteger(value))
        throw new ContractViolation(`${name} must be an integer.`);

    return value;
}

function requiredNonNegativeInteger(value, name) {
    const result = requiredInteger(value, name);
    if (result < 0)
        throw new ContractViolation(`${name} must be non-negative.`);

    return result;
}

function requiredFiniteNumber(value, name) {
    if (typeof value !== 'number' || !Number.isFinite(value))
        throw new ContractViolation(`${name} must be a finite number.`);

    return value;
}

function isNonNegativeInteger(value) {
    return Number.isInteger(value) && value >= 0;
}

function setElementHidden(element, hidden, displayMode) {
    element.hidden = hidden;
    element.style.display = hidden ? 'none' : displayMode;
}
