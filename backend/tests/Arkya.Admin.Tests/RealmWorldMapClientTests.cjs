const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const script = fs.readFileSync(path.resolve(__dirname, "../../src/Arkya.Admin/wwwroot/realm-map.js"), "utf8");
const observedAt = "2026-10-24T12:00:00Z";
const entity = (id, kind, name, x = 30, z = 40, authorityEpoch = 1) => ({ id, kind, name, x, y: 5, z, authorityEpoch });
const server = (serverId, islandId, entities, extra = {}) => ({ serverId, islandId, entities, status: "live", updatedAt: observedAt, truncated: false, totalEntities: entities.length, ...extra });
const report = servers => ({ realmId: "realm-test", observedAt, refreshSeconds: 2, staleAfterSeconds: 10, servers });

async function mount(initial) {
    let activeElement = null, elapsed = 0, nextResponse = initial, calls = 0, maxActive = 0, active = 0, timerId = 0;
    const timers = new Map(), intervals = new Map();
    class Element {
        constructor(tag = "div") { this.tag = tag; this.children = []; this.dataset = {}; this.attributes = {}; this.listeners = {}; this._text = ""; this.className = ""; }
        set textContent(value) { this._text = String(value); this.children = []; }
        get textContent() { return this._text + this.children.map(child => child.textContent).join(""); }
        append(...children) { this.children.push(...children); }
        replaceChildren(...children) { this._text = ""; this.children = children; }
        setAttribute(key, value) { this.attributes[key] = String(value); if (key === "class") this.className = value; }
        addEventListener(type, callback) { this.listeners[type] = callback; }
        querySelectorAll() { return this.children.flatMap(child => [child, ...child.querySelectorAll()]).filter(child => child.dataset.entityKey); }
        get classList() { return { contains: name => this.className.split(" ").includes(name) }; }
        focus() { activeElement = this; }
    }
    const root = new Element(), controls = {};
    for (const name of ["status", "markers", "entities", "directory-note", "details", "servers", "search"])
        controls[name] = new Element();
    controls.search.value = "";
    const filters = ["player", "npc", "creature"].map(kind => Object.assign(new Element("input"), { checked: true, dataset: { mapKind: kind } }));
    const counts = Object.fromEntries(filters.map(filter => [filter.dataset.mapKind, new Element()]));
    root.dataset = { realmId: "realm-test", islandId: "area-a", fixedProfile: "false", serverIds: '["a-1","a-2"]', originX: "10", originZ: "20", width: "100", depth: "100", mapEndpoint: "/map" };
    root.querySelector = selector => {
        if (selector.startsWith("[data-count=")) return counts[selector.match(/"(.*?)"/)[1]];
        return controls[selector.slice(10, -1)];
    };
    root.querySelectorAll = selector => selector === "[data-map-kind]" ? filters : [...controls.markers.querySelectorAll(), ...controls.entities.querySelectorAll()];
    const document = { hidden: false, get activeElement() { return activeElement; }, querySelector: () => root, createElement: tag => new Element(tag), createElementNS: (_, tag) => new Element(tag), listeners: {}, addEventListener(type, callback) { this.listeners[type] = callback; } };
    const context = {
        document, window: { addEventListener() {} }, AbortController, performance: { now: () => elapsed },
        setTimeout: (callback, delay) => { const id = ++timerId; timers.set(id, { callback, delay }); return id; },
        clearTimeout: id => timers.delete(id), setInterval: callback => { const id = ++timerId; intervals.set(id, callback); return id; }, clearInterval: id => intervals.delete(id),
        fetch: async () => {
            calls++; active++; maxActive = Math.max(active, maxActive);
            try {
                const value = typeof nextResponse === "function" ? await nextResponse() : nextResponse;
                if (value instanceof Error) throw value;
                return { ok: true, status: 200, redirected: false, json: async () => value };
            } finally { active--; }
        }
    };
    vm.runInNewContext(script, context);
    const flush = async () => { for (let index = 0; index < 4; index++) await new Promise(resolve => setImmediate(resolve)); };
    await flush();
    return { controls, filters, counts, document, flush, get calls() { return calls; }, get maxActive() { return maxActive; },
        advance(milliseconds) { elapsed += milliseconds; for (const callback of intervals.values()) callback(); },
        async refresh(value) {
            nextResponse = value;
            const poll = [...timers.entries()].find(([, timer]) => timer.delay === 2000);
            assert.ok(poll, "A completed request schedules the next poll"); timers.delete(poll[0]); poll[1].callback(); await flush();
        }
    };
}

test("positions use the selected Area origin, Z axis, safe labels and inspectable details", async () => {
    const ui = await mount(report([server("a-1", "area-a", [entity("p1", "player", "<img onerror=alert(1)>")]), server("a-2", "area-a", [])]));
    const marker = ui.controls.markers.children[0];
    assert.equal(marker.attributes.cx, "20");
    assert.equal(marker.attributes.cy, "80");
    assert.equal(marker.children[0].textContent, "<img onerror=alert(1)> · Player · 30.00 / 5.00 / 40.00");
    assert.equal(ui.counts.player.textContent, "1");
    marker.listeners.click();
    assert.match(ui.controls.details.textContent, /30\.00 \/ 5\.00 \/ 40\.00/);
    assert.match(ui.controls.details.textContent, /a-1/);
    assert.equal(marker.attributes["aria-pressed"], "true");
});

test("highest authority wins before Area filtering and NPCs cannot cross Areas", async () => {
    const ui = await mount(report([
        server("a-1", "area-a", [entity("same-player", "player", "Old owner", 30, 40, 2), entity("npc-a", "npc", "Resident")]),
        server("a-2", "area-a", [entity("local-player", "player", "Old epoch", 30, 40, 1)]),
        server("b-1", "area-b", [entity("same-player", "player", "New owner", 30, 40, 3), entity("npc-b", "npc", "Other Area")]),
        server("a-1", "area-a", [entity("local-player", "player", "Latest epoch", 30, 40, 8)])
    ]));
    assert.equal(ui.counts.player.textContent, "1");
    assert.equal(ui.counts.npc.textContent, "1");
    assert.match(ui.controls.entities.textContent, /Latest epoch/);
    assert.doesNotMatch(ui.controls.entities.textContent, /Old owner|New owner|Other Area|Old epoch/);
});

test("kind filters and search update map markers, list and selected details", async () => {
    const ui = await mount(report([server("a-1", "area-a", [entity("p1", "player", "Alice"), entity("n1", "npc", "Town resident"), entity("c1", "creature", "Wolf")])]));
    assert.equal(ui.controls.markers.children.length, 3);
    ui.filters[0].checked = false; ui.filters[0].listeners.change();
    assert.equal(ui.controls.markers.children.length, 2);
    ui.controls.search.value = "WOLF"; ui.controls.search.listeners.input();
    assert.equal(ui.controls.markers.children.length, 1);
    assert.equal(ui.controls.markers.children[0].tag, "rect");
    assert.match(ui.controls.entities.textContent, /Wolf/);
    assert.doesNotMatch(ui.controls.entities.textContent, /Alice|Town resident/);
});

test("stale positions expire while HTTP is pending; failed refresh clears markers and counts", async () => {
    const ui = await mount(report([server("a-1", "area-a", [entity("p1", "player", "Alice")])]));
    ui.advance(11000);
    assert.equal(ui.controls.markers.children.length, 0);
    assert.match(ui.controls.servers.textContent, /Stale — positions hidden/);
    await ui.refresh(new Error("Network failed"));
    assert.equal(ui.controls.markers.children.length, 0);
    assert.equal(ui.counts.player.textContent, "—");
    assert.equal(ui.controls.status.dataset.state, "error");
});

test("polling is sequential and partial or unsupported reports remain explicit", async () => {
    const ui = await mount(report([server("a-1", "area-a", [entity("p1", "player", "Alice")], { truncated: true, totalEntities: 20 }),
        server("a-2", "area-a", [], { status: "unsupported", updatedAt: null })]));
    assert.match(ui.controls.servers.textContent, /Partial report: 1 of 20/);
    assert.match(ui.controls.servers.textContent, /telemetry unsupported/);
    let complete;
    await ui.refresh(() => new Promise(resolve => { complete = resolve; }));
    ui.document.listeners.visibilitychange(); ui.document.listeners.visibilitychange();
    assert.equal(ui.calls, 2);
    assert.equal(ui.maxActive, 1);
    complete(report([])); await ui.flush();
});

test("response latency counts toward the stale deadline", async () => {
    const ui = await mount(report([server("a-1", "area-a", [entity("p1", "player", "Alice")])]));
    let complete;
    await ui.refresh(() => new Promise(resolve => { complete = resolve; }));
    ui.advance(8000);
    complete(report([server("a-1", "area-a", [entity("p1", "player", "Alice")])]));
    await ui.flush();
    assert.equal(ui.controls.markers.children.length, 1);
    ui.advance(2100);
    assert.equal(ui.controls.markers.children.length, 0);
    assert.match(ui.controls.servers.textContent, /Stale — positions hidden/);
});
