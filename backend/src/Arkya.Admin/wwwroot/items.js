(() => {
    const editor = document.querySelector('[data-item-editor]');
    if (!editor) return;
    const library = editor.querySelector('[data-item-assets]');
    const guidInput = editor.querySelector('[data-asset-guid]');
    const grid = editor.querySelector('[data-asset-grid]');
    const search = editor.querySelector('[data-asset-search]');
    const status = editor.querySelector('[data-asset-status]');
    const previous = editor.querySelector('[data-asset-previous]');
    const next = editor.querySelector('[data-asset-next]');
    let offset = 0;
    let query = '';
    let pending = false;
    const thumbnailUrl = guid => {
        const url = new URL(library.dataset.thumbnailUrl, location.origin);
        url.searchParams.set('guid', guid);
        return url.href;
    };
    const image = asset => {
        if (!asset.hasThumbnail) {
            const mark = document.createElement('span');
            mark.textContent = '◇';
            mark.setAttribute('aria-hidden', 'true');
            return mark;
        }
        const img = document.createElement('img');
        img.src = thumbnailUrl(asset.guid);
        img.alt = '';
        img.loading = 'lazy';
        return img;
    };
    grid.addEventListener('click', event => {
        const card = event.target.closest('[data-guid]');
        if (!card || !grid.contains(card)) return;
        guidInput.value = card.dataset.guid;
        for (const button of grid.querySelectorAll('[aria-pressed]'))
            button.setAttribute('aria-pressed', button === card ? 'true' : 'false');
        editor.querySelector('[data-selected-name]').textContent = card.dataset.name;
        editor.querySelector('[data-selected-path]').textContent = card.dataset.path;
        const kind = editor.querySelector('[data-selected-kind]');
        kind.textContent = card.dataset.kind.toUpperCase();
        kind.hidden = false;
        editor.querySelector('[data-selected-image]').replaceChildren(image({ guid: card.dataset.guid, hasThumbnail: card.dataset.thumbnail === 'true' }));
        status.textContent = 'Selected model: ' + card.dataset.name + '. Save the item to retain this choice.';
    });
    const render = asset => {
        const card = document.createElement('button');
        card.type = 'button';
        card.className = 'item-asset-card';
        card.dataset.guid = asset.guid;
        card.dataset.name = asset.name;
        card.dataset.path = asset.assetPath;
        card.dataset.kind = asset.kind;
        card.dataset.thumbnail = String(asset.hasThumbnail);
        card.setAttribute('aria-pressed', String(asset.guid === guidInput.value));
        const preview = document.createElement('span');
        preview.className = 'item-asset-image';
        preview.append(image(asset));
        card.append(preview);
        for (const [className, value] of [['item-asset-name', asset.name], ['item-asset-kind', asset.kind.toUpperCase()], ['item-asset-path mono', asset.assetPath]]) {
            const span = document.createElement('span');
            span.className = className;
            span.textContent = value;
            if (value === asset.assetPath) span.title = value;
            card.append(span);
        }
        return card;
    };
    const load = async (newOffset, newQuery) => {
        if (pending) return;
        pending = true;
        const hadPrevious = previous.disabled;
        const hadNext = next.disabled;
        previous.disabled = next.disabled = true;
        grid.setAttribute('aria-busy', 'true');
        status.textContent = 'Loading models…';
        try {
            const url = new URL(library.dataset.assetsUrl, location.origin);
            url.searchParams.set('offset', newOffset);
            if (newQuery) url.searchParams.set('search', newQuery);
            const response = await fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' }, redirect: 'error' });
            if (!response.ok) throw new Error();
            const page = await response.json();
            grid.replaceChildren(...page.assets.map(render));
            offset = newOffset;
            query = newQuery;
            previous.disabled = offset === 0;
            next.disabled = !page.hasMore;
            status.textContent = page.assets.length ? 'Select a thumbnail to associate the model.' : 'No models match this search. Export the library from Unity or try another name.';
        } catch {
            previous.disabled = hadPrevious;
            next.disabled = hadNext;
            status.textContent = 'The library is unavailable. Try again. Your changes are retained.';
        } finally {
            pending = false;
            grid.removeAttribute('aria-busy');
        }
    };
    editor.querySelector('[data-asset-search-button]').addEventListener('click', () => load(0, search.value.trim()));
    search.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); load(0, search.value.trim()); } });
    previous.addEventListener('click', () => load(Math.max(0, offset - 24), query));
    next.addEventListener('click', () => load(offset + 24, query));
    const category = editor.querySelector('[data-item-category]');
    const slot = editor.querySelector('[data-item-slot]');
    const stack = editor.querySelector('[data-item-stack]');
    category.addEventListener('change', () => {
        const equipment = ['weapon', 'armor', 'tool'].includes(category.value);
        const allowed = category.value === 'armor' ? ['head', 'chest', 'hands', 'legs', 'feet', 'neck', 'shoulders', 'back', 'waist', 'finger'] : equipment ? ['main-hand', 'off-hand'] : ['none'];
        if (!allowed.includes(slot.value)) slot.value = category.value === 'armor' ? 'chest' : allowed[0];
        if (equipment) stack.value = '1';
    });
})();
