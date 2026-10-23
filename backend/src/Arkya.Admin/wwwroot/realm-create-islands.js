(() => {
    "use strict";

    const catalog = document.querySelector("[data-island-catalog-id]");
    if (!catalog) return;
    const labels = JSON.parse(catalog.dataset.partitionLabels);
    const number = input => input.value === "" ? NaN : Number(input.value);
    const validCount = value => Number.isInteger(value) && value >= 1 && value <= 64;

    function automaticGrid(count, width, depth) {
        let columns = 1;
        let bestScore = Infinity;
        for (let candidate = 1; candidate <= count; candidate++) {
            if (count % candidate !== 0) continue;
            const score = Math.abs(Math.log((width / candidate) / (depth / (count / candidate))));
            if (score < bestScore - 1e-12 || Math.abs(score - bestScore) <= 1e-12 && candidate > columns) {
                columns = candidate;
                bestScore = score;
            }
        }
        return { columns, rows: count / columns };
    }

    for (const editor of document.querySelectorAll("[data-island-editor]")) {
        const find = selector => editor.querySelector(selector);
        const useGrid = find("[data-use-grid]");
        const columnsInput = find("[data-grid-columns]");
        const rowsInput = find("[data-grid-rows]");
        const countInput = find("[data-dedicated-count]");
        const widthInput = find("[data-island-width]");
        const depthInput = find("[data-island-depth]");
        const image = find("[data-island-image]");
        const placeholder = find("[data-island-placeholder]");
        const cells = find("[data-grid-cells]");
        const preview = find("[data-island-preview]");

        function update() {
            const id = find("[data-island-id]").value;
            const known = id === catalog.dataset.islandCatalogId && catalog.dataset.islandCatalogImage;
            const label = known ? catalog.dataset.islandCatalogLabel : id || "Nouvelle île";
            find("[data-island-label]").textContent = label;
            image.hidden = !known;
            placeholder.hidden = Boolean(known);
            if (known) {
                if (image.getAttribute("src") !== catalog.dataset.islandCatalogImage)
                    image.setAttribute("src", catalog.dataset.islandCatalogImage);
            } else image.removeAttribute("src");

            const width = number(widthInput);
            const depth = number(depthInput);
            const imageMatchesPlan = known && catalog.dataset.islandCatalogMapped === "true" && width === 1536 && depth === 1536 &&
                number(find("[data-island-origin-x]")) === 0 && number(find("[data-island-origin-z]")) === 0;
            find("[data-island-image-note]").textContent = !known ? "Aucune image de catalogue pour cet identifiant."
                : imageMatchesPlan ? "Carte du relief · X vers la droite, Y / Z vers le haut."
                : "Illustration · dimensions du plan différentes de la vue.";
            const validDimensions = Number.isFinite(width) && width >= 1 && width <= 1000000 &&
                Number.isFinite(depth) && depth >= 1 && depth <= 1000000;
            let grid = null;
            countInput.readOnly = useGrid.checked;
            find("[data-grid-legacy-note]").hidden = useGrid.checked;
            if (useGrid.checked) {
                const columns = number(columnsInput);
                const rows = number(rowsInput);
                if (validCount(columns) && validCount(rows)) {
                    const total = columns * rows;
                    countInput.value = String(total);
                    if (total <= 64 && validDimensions) grid = { columns, rows };
                }
            } else {
                const count = number(countInput);
                if (validCount(count) && validDimensions) grid = automaticGrid(count, width, depth);
            }

            const summary = grid
                ? `${grid.columns} × ${grid.rows} = ${grid.columns * grid.rows} dédié(s)`
                : "Corrigez les dimensions ou la grille pour afficher l’aperçu (64 dédiés maximum).";
            find("[data-grid-summary]").textContent = summary;
            preview.setAttribute("aria-label", `${label} : ${summary}. X vers la droite, Y / Z vers le haut.`);
            cells.replaceChildren();
            cells.parentElement.classList.toggle("is-dense", Boolean(grid && (grid.columns > 12 || grid.rows > 12)));
            if (!grid) return;
            for (let row = grid.rows - 1; row >= 0; row--) {
                const line = document.createElement("tr");
                for (let column = 0; column < grid.columns; column++) {
                    const cell = document.createElement("td");
                    const label = document.createElement("span");
                    label.textContent = labels[row * grid.columns + column];
                    cell.append(label);
                    line.append(cell);
                }
                cells.append(line);
            }
        }

        useGrid.addEventListener("change", () => {
            if (useGrid.checked && (!validCount(number(columnsInput)) || !validCount(number(rowsInput)))) {
                const count = validCount(number(countInput)) ? number(countInput) : 1;
                const width = number(widthInput) > 0 ? number(widthInput) : 1000;
                const depth = number(depthInput) > 0 ? number(depthInput) : 1000;
                const grid = automaticGrid(count, width, depth);
                columnsInput.value = String(grid.columns);
                rowsInput.value = String(grid.rows);
            }
            update();
        });
        editor.addEventListener("input", update);
        image.addEventListener("error", () => {
            image.hidden = true;
            placeholder.hidden = false;
        });
        update();
    }
})();
