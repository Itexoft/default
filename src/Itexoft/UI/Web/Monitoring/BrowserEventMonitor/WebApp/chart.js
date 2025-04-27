const readablePointSpacingPx = 4;
const xPaddingFraction = 0.05;
const yPaddingFraction = 0.05;
const minTimeSeedMs = 1000;
const topInsetPx = 18;
const sideInsetPx = 14;
const bottomInsetPx = 12;
const provisionalAxisWidthPx = 68;
const bottomAxisHeightPx = 56;
const primaryTickGapPx = 86;
const hitRadiusPx = 12;
const annotationRadiusPx = 11;
const previewGapPx = 8;
const previewPaddingXPx = 8;
const previewPaddingYPx = 5;
const plotPanDivisor = 720;
const zoomRate = 0.0016;
const axisFont = '12px "IBM Plex Mono", "SFMono-Regular", Menlo, monospace';
const axisSecondaryFont = '11px "IBM Plex Mono", "SFMono-Regular", Menlo, monospace';
const previewFont = '12px "Avenir Next", "Segoe UI", sans-serif';
const primaryTimeFormatter = new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
});
const shortTimeFormatter = new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
});
const dayFormatter = new Intl.DateTimeFormat(undefined, {
    day: '2-digit',
    month: 'short',
});
const monthFormatter = new Intl.DateTimeFormat(undefined, {
    month: 'short',
    year: 'numeric',
});
const fullTimeFormatter = new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    timeZoneName: 'short',
});

const timeSteps = [
    {unit: 'second', count: 1, approxMs: 1000},
    {unit: 'second', count: 5, approxMs: 5000},
    {unit: 'second', count: 10, approxMs: 10000},
    {unit: 'second', count: 15, approxMs: 15000},
    {unit: 'second', count: 30, approxMs: 30000},
    {unit: 'minute', count: 1, approxMs: 60000},
    {unit: 'minute', count: 5, approxMs: 300000},
    {unit: 'minute', count: 10, approxMs: 600000},
    {unit: 'minute', count: 15, approxMs: 900000},
    {unit: 'minute', count: 30, approxMs: 1800000},
    {unit: 'hour', count: 1, approxMs: 3600000},
    {unit: 'hour', count: 3, approxMs: 10800000},
    {unit: 'hour', count: 6, approxMs: 21600000},
    {unit: 'hour', count: 12, approxMs: 43200000},
    {unit: 'day', count: 1, approxMs: 86400000},
    {unit: 'day', count: 2, approxMs: 172800000},
    {unit: 'week', count: 1, approxMs: 604800000},
    {unit: 'month', count: 1, approxMs: 2629800000},
    {unit: 'month', count: 3, approxMs: 7889400000},
    {unit: 'month', count: 6, approxMs: 15778800000},
    {unit: 'year', count: 1, approxMs: 31557600000},
    {unit: 'year', count: 5, approxMs: 157788000000},
];

export class BrowserEventMonitorChart {
    constructor(elements, callbacks) {
        this.elements = elements;
        this.callbacks = callbacks;
        this.baseContext = elements.baseCanvas.getContext('2d');
        this.overlayContext = elements.overlayCanvas.getContext('2d');
        this.devicePixelRatio = window.devicePixelRatio || 1;
        this.size = {width: 0, height: 0};
        this.pointerClient = null;
        this.hover = null;
        this.stickyHover = null;
        this.renderModel = null;
        this.baseDirty = true;
        this.overlayDirty = true;
        this.frameHandle = 0;
        this.resizeObserver = new ResizeObserver(() => {
            this.syncCanvasSize();
            this.requestRender(true);
        });
        this.resizeObserver.observe(elements.shell);
        elements.overlayCanvas.addEventListener('mousemove', event => this.handlePointerMove(event));
        elements.overlayCanvas.addEventListener('mouseleave', () => this.handlePointerLeave());
        elements.overlayCanvas.addEventListener('wheel', event => this.handleWheel(event), {passive: false});
        elements.logInput.addEventListener('change', () => this.handleLogToggle());
        this.syncCanvasSize();
    }

    requestRender(baseDirty = false) {
        this.baseDirty ||= baseDirty;
        this.overlayDirty = true;

        if (this.frameHandle !== 0)
            return;

        this.frameHandle = window.requestAnimationFrame(() => {
            this.frameHandle = 0;
            this.render();
        });
    }

    render() {
        const state = this.callbacks.getState();
        const category = state.activeKey ? state.categoriesByKey[state.activeKey] ?? null : null;
        this.syncChrome(state, category);

        if (!category) {
            this.renderModel = null;
            this.clearCanvas(this.baseContext);
            this.clearCanvas(this.overlayContext);
            this.hideTooltip();
            this.hover = null;
            this.stickyHover = null;

            return;
        }

        if (this.baseDirty || !this.renderModel || this.renderModel.categoryKey !== category.key) {
            this.baseDirty = false;
            const previousHover = this.hover ?? this.stickyHover;
            const previousCategoryKey = this.renderModel?.categoryKey ?? null;
            this.renderModel = buildRenderModel(category, this.size, this.baseContext);
            drawBase(this.baseContext, this.renderModel);
            if (previousCategoryKey !== category.key)
                this.stickyHover = null;
            this.hover = this.restoreHover(previousHover)
                ?? this.pickHoverFromPointer()
                ?? this.preserveHover(previousHover);
            this.stickyHover = this.hover ?? this.stickyHover;
        }

        if (!this.overlayDirty)
            return;

        this.overlayDirty = false;
        drawOverlay(this.overlayContext, this.renderModel, this.hover ?? this.stickyHover);
        this.updateTooltip();
    }

    syncChrome(state, category) {
        this.elements.statusBadge.textContent = state.statusText;
        this.elements.statusBadge.className = `monitor-status ${state.statusClass}`;
        setElementHidden(this.elements.logToggle, !category || !category.metadata.allValuesPositive, 'inline-flex');
        setElementHidden(this.elements.message, !!category && category.events.length > 0, 'grid');
        this.elements.message.textContent = category ? 'No events yet' : '';

        if (category && category.metadata.allValuesPositive) {
            this.elements.logInput.checked = !!category.logYEnabled;
        } else {
            this.elements.logInput.checked = false;
        }
    }

    syncCanvasSize() {
        const rect = this.elements.shell.getBoundingClientRect();
        const width = Math.max(1, Math.round(rect.width));
        const height = Math.max(1, Math.round(rect.height));
        if (width === this.size.width && height === this.size.height)
            return;

        this.size = {width, height};
        this.devicePixelRatio = window.devicePixelRatio || 1;
        resizeCanvas(this.elements.baseCanvas, width, height, this.devicePixelRatio);
        resizeCanvas(this.elements.overlayCanvas, width, height, this.devicePixelRatio);
        this.baseDirty = true;
        this.overlayDirty = true;
    }

    handlePointerMove(event) {
        if (!this.renderModel)
            return;

        this.pointerClient = {x: event.clientX, y: event.clientY};
        this.hover = this.pickHoverFromPointer();
        this.stickyHover = this.hover;
        this.requestRender(false);
    }

    handlePointerLeave() {
        this.pointerClient = null;
        this.hover = null;
        this.stickyHover = null;
        this.hideTooltip();
        this.requestRender(false);
    }

    handleWheel(event) {
        if (!this.renderModel)
            return;

        const point = toLocalPoint(this.elements.overlayCanvas, event);
        const zone = getWheelZone(this.renderModel.layout, point);
        if (zone === null)
            return;

        event.preventDefault();
        const category = this.callbacks.getActiveCategory();
        if (!category)
            return;

        if (zone === 'plot') {
            const delta = event.deltaY + event.deltaX;
            const span = this.renderModel.viewX.maxUtcMs - this.renderModel.viewX.minUtcMs;
            const shift = span * (delta / plotPanDivisor);
            this.callbacks.patchCategory(category.key, current => ({
                ...current,
                xView: clampManualXView(
                    current.events,
                    {
                        mode: 'manual',
                        minUtcMs: this.renderModel.viewX.minUtcMs + shift,
                        maxUtcMs: this.renderModel.viewX.maxUtcMs + shift,
                    }),
            }));
        } else if (zone === 'bottom-axis') {
            const next = zoomTimeView(this.renderModel, point.x, Math.exp(event.deltaY * zoomRate));
            this.callbacks.patchCategory(category.key, current => ({
                ...current,
                xView: clampManualXView(current.events, next),
            }));
        } else {
            const next = zoomValueView(this.renderModel, point.y, Math.exp(event.deltaY * zoomRate));
            this.callbacks.patchCategory(category.key, current => ({
                ...current,
                yView: next,
            }));
        }

        this.baseDirty = true;
        this.requestRender(true);
    }

    handleLogToggle() {
        const category = this.callbacks.getActiveCategory();
        if (!category || !category.metadata.allValuesPositive)
            return;

        const enabled = this.elements.logInput.checked;
        this.callbacks.patchCategory(category.key, current => ({
            ...current,
            logYEnabled: enabled,
            yView: {
                mode: 'autoFitVisible',
                minValue: current.yView.minValue,
                maxValue: current.yView.maxValue,
            },
        }));
        this.baseDirty = true;
        this.requestRender(true);
    }

    updateTooltip() {
        const hover = this.hover ?? this.stickyHover;
        if (!this.renderModel || !hover) {
            this.hideTooltip();

            return;
        }

        setElementHidden(this.elements.tooltip, false, 'block');
        this.elements.tooltip.textContent = formatTooltip(hover);
        const shellRect = this.elements.shell.getBoundingClientRect();
        const tooltipRect = this.elements.tooltip.getBoundingClientRect();
        const left = clamp(hover.anchorX + 18, 10, shellRect.width - tooltipRect.width - 10);
        const top = clamp(hover.anchorY + 18, 10, shellRect.height - tooltipRect.height - 10);
        this.elements.tooltip.style.left = `${left}px`;
        this.elements.tooltip.style.top = `${top}px`;
    }

    hideTooltip() {
        setElementHidden(this.elements.tooltip, true, 'block');
    }

    pickHoverFromPointer() {
        if (!this.renderModel || !this.pointerClient)
            return null;

        const rect = this.elements.overlayCanvas.getBoundingClientRect();
        const x = this.pointerClient.x - rect.left;
        const y = this.pointerClient.y - rect.top;
        return pickHover(this.renderModel, x, y);
    }

    restoreHover(previousHover) {
        if (!this.renderModel || !previousHover)
            return null;

        if (previousHover.kind === 'cluster')
            return restoreClusterHover(this.renderModel, previousHover.members);

        return restoreEventHover(this.renderModel, previousHover.members[0] ?? null);
    }

    preserveHover(previousHover) {
        if (!this.pointerClient || !previousHover)
            return null;

        return previousHover;
    }

    clearCanvas(context) {
        context.save();
        context.setTransform(1, 0, 0, 1, 0, 0);
        context.clearRect(0, 0, this.size.width * this.devicePixelRatio, this.size.height * this.devicePixelRatio);
        context.restore();
    }
}

function resizeCanvas(canvas, width, height, dpr) {
    canvas.width = Math.max(1, Math.round(width * dpr));
    canvas.height = Math.max(1, Math.round(height * dpr));
    const context = canvas.getContext('2d');
    context.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function buildRenderModel(category, size, context) {
    const events = category.events;
    const layoutBase = {
        left: sideInsetPx,
        top: topInsetPx,
        right: size.width - provisionalAxisWidthPx - sideInsetPx,
        bottom: size.height - bottomAxisHeightPx - bottomInsetPx,
    };
    const viewX = resolveXView(events, category.xView, layoutBase.right - layoutBase.left);
    const visibleSlice = sliceVisibleEvents(events, viewX.minUtcMs, viewX.maxUtcMs);
    const provisionalY = resolveYView(visibleSlice, category.yView, category.logYEnabled);
    const provisionalTicks = category.logYEnabled ? buildLogTicks(provisionalY) : buildLinearTicks(provisionalY);
    const axisWidth = measureAxisWidth(context, provisionalTicks.labels);
    const layout = {
        left: sideInsetPx,
        top: topInsetPx,
        right: size.width - axisWidth - sideInsetPx,
        bottom: size.height - bottomAxisHeightPx - bottomInsetPx,
    };
    const finalX = resolveXView(events, category.xView, layout.right - layout.left);
    const visibleEvents = sliceVisibleEvents(events, finalX.minUtcMs, finalX.maxUtcMs);
    const viewY = resolveYView(visibleEvents, category.yView, category.logYEnabled);
    const yTicks = category.logYEnabled ? buildLogTicks(viewY) : buildLinearTicks(viewY);
    const xTicks = buildTimeTicks(finalX, layout, context);
    for (const tick of yTicks.items)
        tick.y = mapY(layout, viewY, tick.value, category.logYEnabled);
    const decimated = decimateEvents(visibleEvents, layout, finalX, viewY, category.logYEnabled);
    const clusters = buildAnnotationClusters(visibleEvents, layout, finalX, viewY, category.logYEnabled);
    const previews = buildPreviews(context, clusters, layout);

    return {
        categoryKey: category.key,
        category,
        layout,
        viewX: finalX,
        viewY,
        visibleEvents,
        decimated,
        xTicks,
        yTicks,
        clusters,
        previews,
    };
}

function drawBase(context, model) {
    const {layout} = model;
    context.clearRect(0, 0, context.canvas.clientWidth, context.canvas.clientHeight);
    context.save();
    context.fillStyle = 'rgba(255, 255, 255, 0.42)';
    roundRect(context, 0.5, 0.5, context.canvas.clientWidth - 1, context.canvas.clientHeight - 1, 22);
    context.fill();
    context.strokeStyle = 'rgba(103, 83, 58, 0.18)';
    context.stroke();
    context.restore();
    drawGrid(context, model);
    drawSeries(context, model);
    drawAnnotations(context, model);
    drawAxes(context, model);
}

function drawOverlay(context, model, hover) {
    context.clearRect(0, 0, context.canvas.clientWidth, context.canvas.clientHeight);
    if (!hover)
        return;

    const {layout} = model;
    context.save();
    context.strokeStyle = 'rgba(15, 118, 110, 0.35)';
    context.setLineDash([4, 4]);
    context.beginPath();
    context.moveTo(layout.left, hover.y);
    context.lineTo(layout.right, hover.y);
    context.moveTo(hover.x, layout.top);
    context.lineTo(hover.x, layout.bottom);
    context.stroke();
    context.setLineDash([]);
    context.fillStyle = '#0f766e';
    context.beginPath();
    context.arc(hover.x, hover.y, hover.kind === 'cluster' ? 6 : 4.5, 0, Math.PI * 2);
    context.fill();
    context.restore();
}

function drawGrid(context, model) {
    const {layout} = model;
    context.save();
    context.strokeStyle = 'rgba(38, 28, 18, 0.1)';
    context.lineWidth = 1;

    for (const tick of model.yTicks.items) {
        context.beginPath();
        context.moveTo(layout.left, tick.y);
        context.lineTo(layout.right, tick.y);
        context.stroke();
    }

    for (const tick of model.xTicks.items) {
        context.beginPath();
        context.moveTo(tick.x, layout.top);
        context.lineTo(tick.x, layout.bottom);
        context.stroke();
    }

    context.restore();
}

function drawSeries(context, model) {
    const points = model.decimated;
    if (points.length === 0)
        return;

    context.save();
    context.strokeStyle = '#0f766e';
    context.lineWidth = 1.8;
    context.beginPath();
    context.moveTo(points[0].x, points[0].y);

    for (let index = 1; index < points.length; index += 1)
        context.lineTo(points[index].x, points[index].y);

    context.stroke();
    if (points.length === 1) {
        context.fillStyle = '#0f766e';
        context.beginPath();
        context.arc(points[0].x, points[0].y, 3.5, 0, Math.PI * 2);
        context.fill();
    }

    context.restore();
}

function drawAnnotations(context, model) {
    context.save();
    context.font = previewFont;
    context.textBaseline = 'middle';

    for (const cluster of model.clusters) {
        context.fillStyle = cluster.members.length > 1 ? '#f59e0b' : '#d97706';
        context.beginPath();
        context.arc(cluster.x, cluster.y, cluster.members.length > 1 ? 4.8 : 3.6, 0, Math.PI * 2);
        context.fill();
        if (cluster.members.length > 1) {
            context.fillStyle = '#fff';
            context.fillText(String(cluster.members.length), cluster.x + 6, cluster.y);
        }
    }

    for (const preview of model.previews) {
        context.fillStyle = 'rgba(255, 250, 241, 0.95)';
        context.strokeStyle = 'rgba(103, 83, 58, 0.24)';
        roundRect(context, preview.left, preview.top, preview.width, preview.height, 9);
        context.fill();
        context.stroke();
        context.fillStyle = '#261c12';
        context.fillText(preview.text, preview.textX, preview.textY);
    }

    context.restore();
}

function drawAxes(context, model) {
    const {layout} = model;
    context.save();
    const axisBandTop = layout.bottom + 1;
    const axisBandHeight = Math.max(0, context.canvas.clientHeight - axisBandTop - 1);
    if (axisBandHeight > 0) {
        context.fillStyle = 'rgba(255, 250, 241, 0.9)';
        context.fillRect(layout.left, axisBandTop, layout.right - layout.left, axisBandHeight);
        context.strokeStyle = 'rgba(103, 83, 58, 0.16)';
        context.beginPath();
        context.moveTo(layout.left, axisBandTop);
        context.lineTo(layout.right, axisBandTop);
        context.stroke();
    }

    context.strokeStyle = 'rgba(38, 28, 18, 0.28)';
    context.beginPath();
    context.moveTo(layout.left, layout.bottom);
    context.lineTo(layout.right, layout.bottom);
    context.lineTo(layout.right, layout.top);
    context.stroke();

    context.font = axisFont;
    context.fillStyle = 'rgba(38, 28, 18, 0.74)';
    context.textBaseline = 'middle';

    for (const tick of model.yTicks.items)
        context.fillText(tick.label, layout.right + 10, tick.y);

    context.textBaseline = 'top';
    for (const tick of model.xTicks.items) {
        context.beginPath();
        context.moveTo(tick.x, layout.bottom);
        context.lineTo(tick.x, layout.bottom + 8);
        context.stroke();
        const primaryLeft = clamp(
            tick.x - tick.primaryWidth / 2,
            layout.left + 6,
            Math.max(layout.left + 6, layout.right - tick.primaryWidth - 32));
        context.fillText(tick.primary, primaryLeft, layout.bottom + 12);
        if (tick.secondary) {
            context.font = axisSecondaryFont;
            context.fillStyle = 'rgba(38, 28, 18, 0.5)';
            const secondaryLeft = clamp(
                tick.x - tick.secondaryWidth / 2,
                layout.left + 6,
                Math.max(layout.left + 6, layout.right - tick.secondaryWidth - 32));
            context.fillText(tick.secondary, secondaryLeft, layout.bottom + 28);
            context.font = axisFont;
            context.fillStyle = 'rgba(38, 28, 18, 0.74)';
        }
    }

    context.restore();
}

function pickHover(model, x, y) {
    if (x < model.layout.left || x > model.layout.right || y < model.layout.top || y > model.layout.bottom)
        return null;

    let bestCluster = null;
    for (const cluster of model.clusters) {
        const distance = Math.hypot(cluster.x - x, cluster.y - y);
        if (distance > hitRadiusPx || bestCluster && distance >= bestCluster.distance)
            continue;

        bestCluster = {distance, cluster};
    }

    const eventHit = pickNearestEvent(model, x, y);
    if (bestCluster && (!eventHit || bestCluster.distance <= eventHit.distance + 1)) {
        return {
            kind: 'cluster',
            anchorX: x,
            anchorY: y,
            x: bestCluster.cluster.x,
            y: bestCluster.cluster.y,
            members: bestCluster.cluster.members,
        };
    }

    if (!eventHit)
        return null;

    return {
        kind: 'event',
        anchorX: x,
        anchorY: y,
        x: eventHit.x,
        y: eventHit.y,
        members: [eventHit.event],
    };
}

function pickNearestEvent(model, x, y) {
    const targetTime = invertX(model.layout, model.viewX, x);
    const events = model.visibleEvents;
    let index = lowerBoundEvents(events, targetTime);
    let best = null;

    for (let direction = -1; direction <= 1; direction += 2) {
        for (let cursor = direction < 0 ? index - 1 : index; cursor >= 0 && cursor < events.length; cursor += direction) {
            const px = mapX(model.layout, model.viewX, events[cursor].timestampUtcMs);
            if (Math.abs(px - x) > hitRadiusPx)
                break;

            const py = mapY(model.layout, model.viewY, events[cursor].value, model.category.logYEnabled);
            const distance = Math.hypot(px - x, py - y);
            if (distance > hitRadiusPx || best && distance >= best.distance)
                continue;

            best = {distance, event: events[cursor], x: px, y: py};
        }
    }

    return best;
}

function restoreEventHover(model, previousEvent) {
    if (!previousEvent)
        return null;

    const event = model.visibleEvents.find(candidate => sameEvent(candidate, previousEvent));
    if (!event)
        return null;

    const x = mapX(model.layout, model.viewX, event.timestampUtcMs);
    const y = mapY(model.layout, model.viewY, event.value, model.category.logYEnabled);
    return {
        kind: 'event',
        anchorX: x,
        anchorY: y,
        x,
        y,
        members: [event],
    };
}

function restoreClusterHover(model, previousMembers) {
    if (!previousMembers || previousMembers.length === 0)
        return null;

    let best = null;
    for (const cluster of model.clusters) {
        let overlap = 0;
        for (const member of previousMembers) {
            if (cluster.members.some(candidate => sameEvent(candidate, member)))
                overlap += 1;
        }

        if (overlap === 0 || best && overlap <= best.overlap)
            continue;

        best = {overlap, cluster};
    }

    if (!best)
        return null;

    return {
        kind: 'cluster',
        anchorX: best.cluster.x,
        anchorY: best.cluster.y,
        x: best.cluster.x,
        y: best.cluster.y,
        members: best.cluster.members,
    };
}

function sameEvent(left, right) {
    return !!left
        && !!right
        && left.timestampUtcMs === right.timestampUtcMs
        && left.value === right.value
        && left.text === right.text;
}

function resolveXView(events, xView, plotWidth) {
    if (events.length === 0)
        return {minUtcMs: 0, maxUtcMs: minTimeSeedMs};

    if (xView.mode === 'manual')
        return clampManualXView(events, xView);

    const readableCount = Math.max(1, Math.floor(plotWidth / readablePointSpacingPx));
    const last = events[events.length - 1];
    if (events.length <= readableCount)
        return padTimeRange(events[0].timestampUtcMs, last.timestampUtcMs, resolveSeedSpan(events));

    const start = events[Math.max(0, events.length - readableCount)].timestampUtcMs;
    const end = last.timestampUtcMs;
    const span = Math.max(end - start, resolveSeedSpan(events));
    const padding = span * xPaddingFraction;

    return {
        minUtcMs: end - span - padding,
        maxUtcMs: end + padding,
    };
}

function clampManualXView(events, view) {
    const span = Math.max(view.maxUtcMs - view.minUtcMs, resolveSeedSpan(events));
    const min = events.length === 0 ? 0 : events[0].timestampUtcMs;
    const max = events.length === 0 ? minTimeSeedMs : events[events.length - 1].timestampUtcMs;
    const padding = Math.max(resolveSeedSpan(events) * xPaddingFraction, minTimeSeedMs * xPaddingFraction);
    let nextMin = view.minUtcMs;
    let nextMax = view.maxUtcMs;

    if (nextMin < min - padding) {
        nextMin = min - padding;
        nextMax = nextMin + span;
    }

    if (nextMax > max + padding) {
        nextMax = max + padding;
        nextMin = nextMax - span;
    }

    return {
        mode: 'manual',
        minUtcMs: nextMin,
        maxUtcMs: nextMax,
    };
}

function resolveYView(visibleEvents, yView, logYEnabled) {
    if (visibleEvents.length === 0)
        return {minValue: 0, maxValue: 1};

    if (yView.mode === 'manual')
        return clampManualYView(yView, logYEnabled);

    let min = visibleEvents[0].value;
    let max = visibleEvents[0].value;
    for (const event of visibleEvents) {
        if (event.value < min)
            min = event.value;
        if (event.value > max)
            max = event.value;
    }

    if (logYEnabled)
        return padLogRange(min, max);

    return padLinearRange(min, max);
}

function clampManualYView(view, logYEnabled) {
    if (logYEnabled)
        return {
            mode: 'manual',
            minValue: Math.max(Number.MIN_VALUE, view.minValue),
            maxValue: Math.max(view.maxValue, view.minValue * 1.0001)
        };

    if (view.maxValue <= view.minValue)
        return {mode: 'manual', minValue: view.minValue - 0.5, maxValue: view.minValue + 0.5};

    return view;
}

function buildLinearTicks(viewY) {
    const range = viewY.maxValue - viewY.minValue;
    const rough = range / 6;
    const step = nextNiceStep(rough);
    const min = Math.floor(viewY.minValue / step) * step;
    const max = Math.ceil(viewY.maxValue / step) * step;
    const formatter = makeValueFormatter(step, Math.max(Math.abs(min), Math.abs(max)));
    const items = [];

    for (let value = min; value <= max + step * 0.5; value += step) {
        items.push({
            value,
            y: 0,
            label: formatter(value),
        });
    }

    return {items, labels: items.map(item => item.label), min, max};
}

function buildLogTicks(viewY) {
    const minExponent = Math.floor(Math.log10(viewY.minValue));
    const maxExponent = Math.ceil(Math.log10(viewY.maxValue));
    const items = [];

    for (let exponent = minExponent; exponent <= maxExponent; exponent += 1) {
        const base = 10 ** exponent;
        for (const multiplier of maxExponent - minExponent <= 2 ? [1, 2, 5] : [1]) {
            const value = base * multiplier;
            if (value < viewY.minValue || value > viewY.maxValue)
                continue;

            items.push({
                value,
                y: 0,
                label: makeValueFormatter(value, Math.abs(value))(value),
            });
        }
    }

    return {items, labels: items.map(item => item.label), min: viewY.minValue, max: viewY.maxValue};
}

function buildTimeTicks(viewX, layout, context) {
    const plotWidth = layout.right - layout.left;
    const span = viewX.maxUtcMs - viewX.minUtcMs;
    const target = Math.max(2, Math.floor(plotWidth / primaryTickGapPx));
    const rawStep = span / target;
    let index = timeSteps.findIndex(step => step.approxMs >= rawStep);
    if (index < 0)
        index = timeSteps.length - 1;

    for (; index < timeSteps.length; index += 1) {
        const ticks = createTimeTicks(viewX, layout, timeSteps[index], context);
        if (ticks.every((tick, tickIndex) => tickIndex === 0 || tick.x - ticks[tickIndex - 1].x >= Math.max(tick.primaryWidth, ticks[tickIndex - 1].primaryWidth) + 12))
            return {items: ticks, step: timeSteps[index]};
    }

    return {
        items: createTimeTicks(viewX, layout, timeSteps[timeSteps.length - 1], context),
        step: timeSteps[timeSteps.length - 1]
    };
}

function createTimeTicks(viewX, layout, step, context) {
    const ticks = [];
    let cursor = alignTime(viewX.minUtcMs, step);

    while (cursor <= viewX.maxUtcMs) {
        const primary = formatTimePrimary(cursor, step);
        const secondary = formatTimeSecondary(cursor, step, ticks.length === 0 ? null : ticks[ticks.length - 1].timeUtcMs);
        ticks.push({
            timeUtcMs: cursor,
            x: mapX(layout, viewX, cursor),
            primary,
            secondary,
            primaryWidth: measureText(context, axisFont, primary),
            secondaryWidth: secondary ? measureText(context, axisSecondaryFont, secondary) : 0,
        });
        cursor = addTime(cursor, step);
    }

    return ticks;
}

function decimateEvents(events, layout, viewX, viewY, logYEnabled) {
    if (events.length <= 2)
        return events.map(event => projectEvent(layout, viewX, viewY, event, logYEnabled));

    const width = Math.max(1, Math.floor(layout.right - layout.left));
    const columns = new Array(width);
    for (const event of events) {
        const projected = projectEvent(layout, viewX, viewY, event, logYEnabled);
        const column = clamp(Math.floor(projected.x - layout.left), 0, width - 1);
        let bucket = columns[column];
        if (!bucket) {
            bucket = {first: projected, last: projected, min: projected, max: projected};
            columns[column] = bucket;
            continue;
        }

        bucket.last = projected;
        if (projected.event.value < bucket.min.event.value)
            bucket.min = projected;
        if (projected.event.value > bucket.max.event.value)
            bucket.max = projected;
    }

    const result = [];
    for (const bucket of columns) {
        if (!bucket)
            continue;

        pushUnique(result, bucket.first);
        pushUnique(result, bucket.min);
        pushUnique(result, bucket.max);
        pushUnique(result, bucket.last);
    }

    result.sort((left, right) => left.event.timestampUtcMs - right.event.timestampUtcMs);

    return result;
}

function buildAnnotationClusters(events, layout, viewX, viewY, logYEnabled) {
    const clusters = [];

    for (const event of events) {
        if (!event.text)
            continue;

        const projected = projectEvent(layout, viewX, viewY, event, logYEnabled);
        let cluster = clusters.find(existing => Math.hypot(existing.x - projected.x, existing.y - projected.y) <= annotationRadiusPx);
        if (!cluster) {
            cluster = {x: projected.x, y: projected.y, members: []};
            clusters.push(cluster);
        }

        cluster.members.push(event);
        cluster.x = (cluster.x * (cluster.members.length - 1) + projected.x) / cluster.members.length;
        cluster.y = (cluster.y * (cluster.members.length - 1) + projected.y) / cluster.members.length;
    }

    clusters.sort((left, right) => right.members[right.members.length - 1].timestampUtcMs - left.members[left.members.length - 1].timestampUtcMs);

    return clusters;
}

function buildPreviews(context, clusters, layout) {
    const dense = clusters.length > Math.max(1, Math.floor((layout.right - layout.left) / 110));
    const accepted = [];
    context.font = previewFont;

    for (const cluster of dense ? clusters.slice(0, 1) : clusters) {
        const label = buildPreviewText(cluster);
        if (!label)
            continue;

        const width = Math.min(220, measureText(context, previewFont, label) + previewPaddingXPx * 2);
        const height = 24;
        const candidates = [
            {left: cluster.x + previewGapPx, top: cluster.y - height - previewGapPx},
            {left: cluster.x - width - previewGapPx, top: cluster.y - height - previewGapPx},
            {left: cluster.x + previewGapPx, top: cluster.y + previewGapPx},
            {left: cluster.x - width - previewGapPx, top: cluster.y + previewGapPx},
        ];

        for (const candidate of candidates) {
            const box = {
                left: clamp(candidate.left, layout.left, layout.right - width),
                top: clamp(candidate.top, layout.top, layout.bottom - height),
                width,
                height,
                text: fitText(context, label, width - previewPaddingXPx * 2),
            };
            box.textX = box.left + previewPaddingXPx;
            box.textY = box.top + height / 2;
            if (accepted.some(existing => boxesOverlap(existing, box)))
                continue;

            accepted.push(box);
            break;
        }
    }

    return accepted;
}

function projectEvent(layout, viewX, viewY, event, logYEnabled) {
    return {
        event,
        x: mapX(layout, viewX, event.timestampUtcMs),
        y: mapY(layout, viewY, event.value, logYEnabled),
    };
}

function mapX(layout, viewX, timestampUtcMs) {
    const scale = (layout.right - layout.left) / (viewX.maxUtcMs - viewX.minUtcMs);
    return layout.left + (timestampUtcMs - viewX.minUtcMs) * scale;
}

function invertX(layout, viewX, x) {
    const scale = (viewX.maxUtcMs - viewX.minUtcMs) / (layout.right - layout.left);
    return viewX.minUtcMs + (x - layout.left) * scale;
}

function mapY(layout, viewY, value, logYEnabled) {
    const domainMin = logYEnabled ? Math.log10(viewY.minValue) : viewY.minValue;
    const domainMax = logYEnabled ? Math.log10(viewY.maxValue) : viewY.maxValue;
    const mapped = logYEnabled ? Math.log10(value) : value;
    const ratio = (mapped - domainMin) / (domainMax - domainMin);
    return layout.bottom - ratio * (layout.bottom - layout.top);
}

function zoomTimeView(model, x, factor) {
    const anchor = invertX(model.layout, model.viewX, x);
    return {
        mode: 'manual',
        minUtcMs: anchor - (anchor - model.viewX.minUtcMs) * factor,
        maxUtcMs: anchor + (model.viewX.maxUtcMs - anchor) * factor,
    };
}

function zoomValueView(model, y, factor) {
    const anchor = invertY(model.layout, model.viewY, y, model.category.logYEnabled);
    if (model.category.logYEnabled) {
        const anchorLog = Math.log10(anchor);
        const minLog = Math.log10(model.viewY.minValue);
        const maxLog = Math.log10(model.viewY.maxValue);
        return {
            mode: 'manual',
            minValue: 10 ** (anchorLog - (anchorLog - minLog) * factor),
            maxValue: 10 ** (anchorLog + (maxLog - anchorLog) * factor),
        };
    }

    return {
        mode: 'manual',
        minValue: anchor - (anchor - model.viewY.minValue) * factor,
        maxValue: anchor + (model.viewY.maxValue - anchor) * factor,
    };
}

function invertY(layout, viewY, y, logYEnabled) {
    const ratio = (layout.bottom - y) / (layout.bottom - layout.top);
    if (logYEnabled) {
        const min = Math.log10(viewY.minValue);
        const max = Math.log10(viewY.maxValue);
        return 10 ** (min + ratio * (max - min));
    }

    return viewY.minValue + ratio * (viewY.maxValue - viewY.minValue);
}

function getWheelZone(layout, point) {
    if (point.x >= layout.left && point.x <= layout.right && point.y >= layout.top && point.y <= layout.bottom)
        return 'plot';
    if (point.x >= layout.left && point.x <= layout.right && point.y > layout.bottom)
        return 'bottom-axis';
    if (point.x > layout.right && point.y >= layout.top && point.y <= layout.bottom)
        return 'right-axis';

    return null;
}

function sliceVisibleEvents(events, minUtcMs, maxUtcMs) {
    if (events.length === 0)
        return [];

    const start = Math.max(0, lowerBoundEvents(events, minUtcMs) - 1);
    const end = Math.min(events.length, upperBoundEvents(events, maxUtcMs) + 1);
    return events.slice(start, end);
}

function lowerBoundEvents(events, value) {
    let left = 0;
    let right = events.length;
    while (left < right) {
        const middle = left + right >> 1;
        if (events[middle].timestampUtcMs < value)
            left = middle + 1;
        else
            right = middle;
    }

    return left;
}

function upperBoundEvents(events, value) {
    let left = 0;
    let right = events.length;
    while (left < right) {
        const middle = left + right >> 1;
        if (events[middle].timestampUtcMs <= value)
            left = middle + 1;
        else
            right = middle;
    }

    return left;
}

function resolveSeedSpan(events) {
    let delta = Number.POSITIVE_INFINITY;
    for (let index = 1; index < events.length; index += 1) {
        const current = events[index].timestampUtcMs - events[index - 1].timestampUtcMs;
        if (current > 0 && current < delta)
            delta = current;
    }

    return Number.isFinite(delta) ? delta : minTimeSeedMs;
}

function padTimeRange(minUtcMs, maxUtcMs, seedSpanMs) {
    const span = Math.max(maxUtcMs - minUtcMs, seedSpanMs);
    const padding = span * xPaddingFraction;
    const center = (minUtcMs + maxUtcMs) / 2;

    return {
        minUtcMs: center - span / 2 - padding,
        maxUtcMs: center + span / 2 + padding,
    };
}

function padLinearRange(min, max) {
    if (min === max) {
        const halfSpan = Math.max(Math.abs(min) * 0.1, 1);
        return {minValue: min - halfSpan, maxValue: max + halfSpan};
    }

    const range = max - min;
    const pad = range * yPaddingFraction;
    let nextMin = min - pad;
    let nextMax = max + pad;

    if (min >= 0 && nextMin <= 0 && min <= pad)
        nextMin = 0;
    if (max <= 0 && nextMax >= 0 && Math.abs(max) <= pad)
        nextMax = 0;

    return {minValue: nextMin, maxValue: nextMax};
}

function padLogRange(min, max) {
    const safeMin = Math.max(Number.MIN_VALUE, min);
    const safeMax = Math.max(safeMin * 1.0001, max);
    if (safeMin === safeMax)
        return {minValue: safeMin / 1.4, maxValue: safeMax * 1.4};

    const minLog = Math.log10(safeMin);
    const maxLog = Math.log10(safeMax);
    const pad = (maxLog - minLog) * yPaddingFraction;

    return {
        minValue: 10 ** (minLog - pad),
        maxValue: 10 ** (maxLog + pad),
    };
}

function nextNiceStep(value) {
    const exponent = Math.floor(Math.log10(Math.max(value, Number.MIN_VALUE)));
    const base = 10 ** exponent;
    const normalized = value / base;
    for (const candidate of [1, 2, 2.5, 5, 10]) {
        if (normalized <= candidate)
            return candidate * base;
    }

    return 10 * base;
}

function makeValueFormatter(step, maxAbs) {
    const scientific = maxAbs >= 1e6 || maxAbs > 0 && maxAbs < 1e-4 || step < 1e-4;
    if (scientific)
        return value => value.toExponential(2).replace('e+', 'e');

    const decimals = Math.max(0, Math.ceil(-Math.log10(step)) + (String(step).includes('.') ? 1 : 0));
    const formatter = new Intl.NumberFormat(undefined, {
        minimumFractionDigits: 0,
        maximumFractionDigits: Math.min(8, decimals),
    });
    return value => formatter.format(value);
}

function alignTime(timestampUtcMs, step) {
    const date = new Date(timestampUtcMs);
    date.setMilliseconds(0);
    if (step.unit === 'second') {
        date.setSeconds(Math.floor(date.getSeconds() / step.count) * step.count);
    } else if (step.unit === 'minute') {
        date.setSeconds(0, 0);
        date.setMinutes(Math.floor(date.getMinutes() / step.count) * step.count);
    } else if (step.unit === 'hour') {
        date.setMinutes(0, 0, 0);
        date.setHours(Math.floor(date.getHours() / step.count) * step.count);
    } else if (step.unit === 'day') {
        date.setHours(0, 0, 0, 0);
        date.setDate(Math.floor((date.getDate() - 1) / step.count) * step.count + 1);
    } else if (step.unit === 'week') {
        date.setHours(0, 0, 0, 0);
        date.setDate(date.getDate() - (date.getDay() + 6) % 7);
    } else if (step.unit === 'month') {
        date.setHours(0, 0, 0, 0);
        date.setDate(1);
        date.setMonth(Math.floor(date.getMonth() / step.count) * step.count);
    } else {
        date.setHours(0, 0, 0, 0);
        date.setMonth(0, 1);
        date.setFullYear(Math.floor(date.getFullYear() / step.count) * step.count);
    }

    return date.getTime();
}

function addTime(timestampUtcMs, step) {
    const date = new Date(timestampUtcMs);
    if (step.unit === 'second')
        date.setSeconds(date.getSeconds() + step.count);
    else if (step.unit === 'minute')
        date.setMinutes(date.getMinutes() + step.count);
    else if (step.unit === 'hour')
        date.setHours(date.getHours() + step.count);
    else if (step.unit === 'day')
        date.setDate(date.getDate() + step.count);
    else if (step.unit === 'week')
        date.setDate(date.getDate() + step.count * 7);
    else if (step.unit === 'month')
        date.setMonth(date.getMonth() + step.count);
    else
        date.setFullYear(date.getFullYear() + step.count);

    return date.getTime();
}

function formatTimePrimary(timestampUtcMs, step) {
    if (step.unit === 'second')
        return primaryTimeFormatter.format(timestampUtcMs);
    if (step.unit === 'minute' || step.unit === 'hour')
        return shortTimeFormatter.format(timestampUtcMs);
    if (step.unit === 'day' || step.unit === 'week')
        return dayFormatter.format(timestampUtcMs);
    if (step.unit === 'month')
        return monthFormatter.format(timestampUtcMs);

    return String(new Date(timestampUtcMs).getFullYear());
}

function formatTimeSecondary(timestampUtcMs, step, previousUtcMs) {
    if (previousUtcMs === null)
        return null;

    const current = new Date(timestampUtcMs);
    const previous = new Date(previousUtcMs);
    if (step.unit === 'second' || step.unit === 'minute' || step.unit === 'hour') {
        if (current.getDate() !== previous.getDate() || current.getMonth() !== previous.getMonth() || current.getFullYear() !== previous.getFullYear())
            return `${dayFormatter.format(timestampUtcMs)} ${current.getFullYear()}`;
        return null;
    }

    if (step.unit === 'day' || step.unit === 'week') {
        if (current.getMonth() !== previous.getMonth() || current.getFullYear() !== previous.getFullYear())
            return monthFormatter.format(timestampUtcMs);
    }

    return null;
}

function buildPreviewText(cluster) {
    if (cluster.members.length > 1)
        return `${cluster.members.length} notes`;

    return cluster.members[0].text?.split('\n', 1)[0].trim().replace(/\s+/g, ' ') ?? '';
}

function fitText(context, text, width) {
    if (measureText(context, previewFont, text) <= width)
        return text;

    let value = text;
    while (value.length > 1 && measureText(context, previewFont, `${value}…`) > width)
        value = value.slice(0, -1);

    return `${value}…`;
}

function formatTooltip(hover) {
    return hover.members
        .map(event => `${fullTimeFormatter.format(event.timestampUtcMs)}\n${event.value}\n${event.text ?? '(no annotation)'}`)
        .join('\n\n');
}

function measureAxisWidth(context, labels) {
    let width = provisionalAxisWidthPx;
    for (const label of labels)
        width = Math.max(width, measureText(context, axisFont, label) + 22);

    return width;
}

function measureText(context, font, text) {
    context.save();
    context.font = font;
    const width = context.measureText(text).width;
    context.restore();

    return width;
}

function boxesOverlap(left, right) {
    return left.left < right.left + right.width
        && left.left + left.width > right.left
        && left.top < right.top + right.height
        && left.top + left.height > right.top;
}

function pushUnique(items, candidate) {
    if (!items.some(item => item.event === candidate.event))
        items.push(candidate);
}

function roundRect(context, x, y, width, height, radius) {
    context.beginPath();
    context.moveTo(x + radius, y);
    context.arcTo(x + width, y, x + width, y + height, radius);
    context.arcTo(x + width, y + height, x, y + height, radius);
    context.arcTo(x, y + height, x, y, radius);
    context.arcTo(x, y, x + width, y, radius);
    context.closePath();
}

function toLocalPoint(element, event) {
    const rect = element.getBoundingClientRect();
    return {
        x: event.clientX - rect.left,
        y: event.clientY - rect.top,
    };
}

function clamp(value, min, max) {
    return Math.min(max, Math.max(min, value));
}

function setElementHidden(element, hidden, displayMode) {
    element.hidden = hidden;
    element.style.display = hidden ? 'none' : displayMode;
}
