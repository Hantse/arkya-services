(() => {
    "use strict";
    const root = document.querySelector("[data-world-map]");
    if (!root) return;
    const status = root.querySelector("[data-map-status]");
    const markers = root.querySelector("[data-map-markers]");
    const directory = root.querySelector("[data-map-entities]");
    const directoryNote = root.querySelector("[data-map-directory-note]");
    const details = root.querySelector("[data-map-details]");
    const serversList = root.querySelector("[data-map-servers]");
    const search = root.querySelector("[data-map-search]");
    const filters = [...root.querySelectorAll("[data-map-kind]")];
    const allowedServers = new Set(JSON.parse(root.dataset.serverIds));
    const originX = Number(root.dataset.originX), originZ = Number(root.dataset.originZ);
    const width = Number(root.dataset.width), depth = Number(root.dataset.depth);
    const kinds = { player: "Player", npc: "NPC", creature: "Creature" };
    const namespace = "http://www.w3.org/2000/svg";
    let snapshot = null, requestedAt = 0, selectedKey = null, displayed = [];
    let timer = null, request = null, stopped = false, failure = null, freshness = "";

    const node = (tag, text, className) => {
        const element = document.createElement(tag);
        if (text !== undefined) element.textContent = text;
        if (className) element.className = className;
        return element;
    };
    const coordinates = entity => [entity.x, entity.y, entity.z].map(value => value.toFixed(2)).join(" / ");
    const date = value => value ? new Date(value).toISOString().replace("T", " ").replace(/\.\d{3}Z$/, " UTC") : "No report received";
    const key = item => item.entity.kind + ":" + (item.entity.kind === "player" ? "" : item.server.serverId + ":") + item.entity.id;
    const selectedArea = server => allowedServers.has(server.serverId) &&
        (server.islandId === root.dataset.islandId || server.islandId == null && root.dataset.fixedProfile === "true");
    const reportIsLive = server => server.status === "live" && Number.isFinite(Date.parse(server.updatedAt)) &&
        Date.parse(server.updatedAt) + snapshot.staleAfterSeconds * 1000 > Date.parse(snapshot.observedAt) + performance.now() - requestedAt;

    function liveEntities() {
        const entities = [], players = new Map();
        for (const server of snapshot.servers) {
            if (!reportIsLive(server)) continue;
            for (const entity of server.entities) {
                if (!entity || !Object.hasOwn(kinds, entity.kind) || typeof entity.id !== "string" || typeof entity.name !== "string" ||
                    ![entity.x, entity.y, entity.z].every(Number.isFinite)) continue;
                const item = { entity, server };
                if (entity.kind !== "player") entities.push(item);
                else {
                    const previous = players.get(entity.id);
                    const epoch = Number.isSafeInteger(entity.authorityEpoch) ? entity.authorityEpoch : 0;
                    const previousEpoch = previous?.entity.authorityEpoch ?? -1;
                    if (!previous || epoch > previousEpoch || epoch === previousEpoch && Date.parse(server.updatedAt) > Date.parse(previous.server.updatedAt))
                        players.set(entity.id, item);
                }
            }
        }
        // Pick the current player authority before filtering Areas during a handoff.
        return [...entities, ...players.values()].filter(item => selectedArea(item.server));
    }

    function showDetails() {
        details.replaceChildren();
        const item = displayed.find(item => key(item) === selectedKey);
        if (!item) {
            details.append(node("p", selectedKey ? "This entity is no longer visible in the latest report." : "Select a player, NPC or creature.", "muted"));
            return;
        }
        const dl = node("dl");
        for (const [label, value] of [["Name", item.entity.name], ["Type", kinds[item.entity.kind]], ["ID", item.entity.id],
            ["Position X / Y / Z", coordinates(item.entity)], ["Dedicated server", item.server.serverId], ["Updated", date(item.server.updatedAt)]]) {
            const row = node("div");
            row.append(node("dt", label), node("dd", value));
            dl.append(row);
        }
        details.append(dl);
    }

    function select(item) {
        selectedKey = key(item);
        for (const element of root.querySelectorAll("[data-entity-key]"))
            element.setAttribute("aria-pressed", String(element.dataset.entityKey === selectedKey));
        showDetails();
    }

    function renderEntities(entities) {
        const focused = document.activeElement?.dataset.entityKey;
        const focusOnMap = document.activeElement?.classList.contains("realm-map-marker");
        const enabledKinds = new Set(filters.filter(filter => filter.checked).map(filter => filter.dataset.mapKind));
        const query = search.value.trim().toLocaleLowerCase();
        displayed = entities.filter(item => enabledKinds.has(item.entity.kind) &&
            (!query || (item.entity.name + " " + item.entity.id).toLocaleLowerCase().includes(query)));
        markers.replaceChildren();
        directory.replaceChildren();
        let outside = 0;
        const radius = Math.min(width, depth) * .008;
        for (const item of displayed.slice(0, 5000)) {
            const entity = item.entity, x = entity.x - originX, z = depth - (entity.z - originZ);
            if (x < 0 || x > width || z < 0 || z > depth) { outside++; continue; }
            let marker;
            if (entity.kind === "player") {
                marker = document.createElementNS(namespace, "circle");
                marker.setAttribute("cx", x); marker.setAttribute("cy", z); marker.setAttribute("r", radius);
            } else {
                marker = document.createElementNS(namespace, "rect");
                marker.setAttribute("x", x - radius); marker.setAttribute("y", z - radius);
                marker.setAttribute("width", radius * 2); marker.setAttribute("height", radius * 2);
                if (entity.kind === "creature") marker.setAttribute("transform", `rotate(45 ${x} ${z})`);
            }
            marker.setAttribute("class", "realm-map-marker " + entity.kind);
            marker.setAttribute("tabindex", "0"); marker.setAttribute("role", "button");
            marker.setAttribute("aria-label", kinds[entity.kind] + " · " + entity.name);
            marker.setAttribute("aria-pressed", String(key(item) === selectedKey));
            marker.dataset.entityKey = key(item);
            const title = document.createElementNS(namespace, "title");
            title.textContent = entity.name + " · " + kinds[entity.kind] + " · " + coordinates(entity);
            marker.append(title);
            marker.addEventListener("click", () => select(item));
            marker.addEventListener("keydown", event => {
                if (event.key === "Enter" || event.key === " ") { event.preventDefault(); select(item); }
            });
            markers.append(marker);
        }
        for (const item of displayed.slice(0, 200)) {
            const li = node("li"), button = node("button"), label = node("span");
            button.type = "button";
            button.dataset.entityKey = key(item); button.setAttribute("aria-pressed", String(key(item) === selectedKey));
            const symbol = node("span", undefined, "realm-map-key " + item.entity.kind);
            symbol.setAttribute("aria-hidden", "true");
            label.append(node("strong", item.entity.name), node("small", kinds[item.entity.kind] + " · " + item.server.serverId));
            button.append(symbol, label); button.addEventListener("click", () => select(item)); li.append(button); directory.append(li);
        }
        const notes = [displayed.length === 0 ? "No matching live entities in this Area." : `${displayed.length} live entities match the filters.`];
        if (outside) notes.push(`${outside} outside the configured map bounds; available in the list.`);
        if (displayed.length > 200) notes.push("The list shows the first 200. Use search to narrow the results.");
        if (displayed.length > 5000) notes.push("The map shows at most 5,000 markers. Use filters to narrow the results.");
        directoryNote.textContent = notes.join(" ");
        showDetails();
        if (focused) {
            const container = focusOnMap ? markers : directory;
            [...container.querySelectorAll("[data-entity-key]")].find(element => element.dataset.entityKey === focused)?.focus({ preventScroll: true });
        }
    }

    function render() {
        if (!snapshot) return;
        const servers = snapshot.servers.filter(selectedArea);
        const entities = liveEntities();
        freshness = snapshot.servers.map(server => reportIsLive(server) ? "1" : "0").join("");
        for (const kind of Object.keys(kinds))
            root.querySelector(`[data-count="${kind}"]`).textContent = entities.filter(item => item.entity.kind === kind).length;
        serversList.replaceChildren();
        for (const serverId of allowedServers) {
            const server = servers.find(server => server.serverId === serverId);
            const state = server && reportIsLive(server) ? "live" : server?.status === "live" ? "stale" : server?.status ?? "unavailable";
            const label = { live: "Live", stale: "Stale — positions hidden", unsupported: "Update required — telemetry unsupported", unavailable: "No telemetry available" }[state] ?? "No telemetry available";
            const li = node("li"); li.append(node("strong", serverId), node("span", label, "realm-map-server-state " + state));
            li.append(node("small", date(server?.updatedAt)));
            if (server?.truncated) li.append(node("small", `Partial report: ${server.entities.length} of ${server.totalEntities} entities.`));
            serversList.append(li);
        }
        const live = servers.filter(reportIsLive).length;
        const partial = servers.some(server => server.truncated);
        status.dataset.state = failure ? "error" : live === allowedServers.size && live > 0 && !partial ? "live" : "warning";
        status.textContent = failure ?? (allowedServers.size === 0 ? "No dedicated server is assigned to this Area." :
            `${live} / ${allowedServers.size} dedicated servers reporting live positions. Last observation: ${date(snapshot.observedAt)}.` +
            (partial ? " Some reports are partial." : "") + (live < allowedServers.size ? " Missing or stale positions are hidden." : ""));
        renderEntities(failure ? [] : entities);
        if (failure) for (const kind of Object.keys(kinds)) root.querySelector(`[data-count="${kind}"]`).textContent = "—";
    }

    async function poll() {
        if (stopped || request) return;
        clearTimeout(timer);
        if (document.hidden) { timer = setTimeout(poll, 2000); return; }
        request = new AbortController();
        const startedAt = performance.now();
        const timeout = setTimeout(() => request?.abort(), 8000);
        try {
            const response = await fetch(root.dataset.mapEndpoint, { credentials: "same-origin", cache: "no-store", signal: request.signal,
                headers: { Accept: "application/json" } });
            if (response.redirected || response.status === 401 || response.status === 403) {
                stopped = true;
                throw new Error("Your administrator session has expired. Sign in again to resume the live map.");
            }
            if (!response.ok) throw new Error("The live map is unavailable. Retrying automatically.");
            const data = await response.json();
            if (data.realmId !== root.dataset.realmId || !Array.isArray(data.servers) || !Number.isFinite(Date.parse(data.observedAt)) ||
                !Number.isFinite(data.staleAfterSeconds) || data.staleAfterSeconds <= 0 ||
                data.servers.some(server => !server || !Array.isArray(server.entities)))
                throw new Error("The live map received an invalid report. Retrying automatically.");
            snapshot = data; requestedAt = startedAt; failure = null;
        } catch (error) {
            if (stopped && error.name === "AbortError") return;
            failure = error.name === "AbortError" ? "The live map request timed out. Retrying automatically." :
                error.message || "The live map is unavailable. Retrying automatically.";
            status.textContent = failure; status.dataset.state = "error";
            markers.replaceChildren(); directory.replaceChildren(); displayed = [];
            directoryNote.textContent = "Live positions are unavailable."; showDetails();
        } finally {
            clearTimeout(timeout); request = null;
            render();
            if (!stopped) timer = setTimeout(poll, 2000);
        }
    }
    for (const filter of filters) filter.addEventListener("change", render);
    search.addEventListener("input", render);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) poll(); });
    window.addEventListener("pagehide", () => { stopped = true; clearTimeout(timer); request?.abort(); });
    // Expire markers even when the next HTTP request is still pending.
    const freshnessTimer = setInterval(() => {
        if (snapshot && snapshot.servers.map(server => reportIsLive(server) ? "1" : "0").join("") !== freshness) render();
    }, 1000);
    window.addEventListener("pagehide", () => clearInterval(freshnessTimer));
    poll();
})();
