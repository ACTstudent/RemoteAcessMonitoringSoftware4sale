/* Local directory navigation. Data attributes opt in real listings, never edit/bulk forms. */
(() => {
    'use strict';
    const normalize = value => String(value ?? '').normalize('NFKD').replace(/[\u0300-\u036f]/g, '').trim().toLocaleLowerCase();
    // Explicitly marked history tables use the same pager as the directories, but
    // page at 15 rows: a log is read by scanning, and 6 makes that mostly paging.
    // Tables with server paging, editable rows or bulk-entry forms are not opted in.
    document.querySelectorAll('table[data-crud-table]').forEach(table => {
        if (table.closest('[data-crud-list]')) return;
        const region = table.closest('.table-responsive') || table;
        const panel = document.createElement('div');
        panel.className = 'crud-panel crud-table-panel';
        panel.setAttribute('data-crud-list', '');
        panel.dataset.crudLabel = table.dataset.crudTable || 'records';
        panel.dataset.crudPageSize = '15';
        // A grouped table carries a hidden detail row after each summary row.
        // Those are not records: counting them would page them as if they were,
        // and the first one would be mistaken for the empty-state placeholder.
        const rows = Array.from(table.tBodies).flatMap(body => Array.from(body.rows))
            .filter(row => !row.hasAttribute('data-log-detail'));
        const placeholder = rows.find(row => row.querySelector('td[colspan]'));
        rows.filter(row => row !== placeholder).forEach(row => row.setAttribute('data-crud-item', ''));
        const empty = document.createElement('div');
        empty.className = 'crud-no-results';
        empty.setAttribute('data-crud-empty', '');
        empty.hidden = true;
        empty.textContent = placeholder?.textContent.trim() || 'No records available.';
        const footer = document.createElement('div');
        footer.className = 'crud-footer';
        footer.setAttribute('data-crud-pagination', '');
        region.before(panel);
        panel.append(region, empty, footer);
    });
    document.querySelectorAll('[data-crud-list]').forEach((panel, index) => {
        const own = selector => Array.from(panel.querySelectorAll(selector)).filter(el => el.closest('[data-crud-list]') === panel);
        const items = own('[data-crud-item]');
        const search = own('[data-crud-search]')[0];
        const filters = own('[data-crud-filter]');
        const footer = own('[data-crud-pagination]')[0];
        const empty = own('[data-crud-empty]')[0];
        if (!footer) return;
        const label = panel.dataset.crudLabel || 'records';
        // A panel marked data-crud-scroll shows everything and scrolls in its own
        // box, rather than splitting the records over numbered pages.
        const scrolls = panel.hasAttribute('data-crud-scroll');
        const pageSize = scrolls
            ? Number.POSITIVE_INFINITY
            : Math.max(1, Number.parseInt(panel.dataset.crudPageSize, 10) || 6);
        const storageKey = `cams.directory:${location.pathname}${location.search}:${index}`;
        // A view writes its empty state as a heading and a sentence beneath it.
        // Reading textContent off the wrapper runs the two together - "No classes
        // yetSelect Create Class to add your first section" - so the parts are
        // read separately and punctuated back into one line.
        const initialEmptyMessage = (() => {
            if (!empty) return undefined;
            const parts = Array.from(empty.children)
                .map(child => child.textContent.trim())
                .filter(Boolean);
            if (parts.length < 2) return empty.textContent.trim();
            return parts
                .map((part, i) => (i === parts.length - 1 || /[.!?:]$/.test(part) ? part : part + '.'))
                .join(' ');
        })();
        let page = 1;
        let matchingItems = items;
        // Only visible record text is indexed; field values, credentials and action labels are excluded.
        const records = items.map(item => {
            const clone = item.cloneNode(true);
            clone.querySelectorAll('input, select, textarea, button, form, .crud-card-actions, .modal').forEach(el => el.remove());
            if (clone.matches('tr')) {
                const last = clone.lastElementChild;
                if (item.lastElementChild?.querySelector('button, form, a')) last?.remove();
            }
            return { element: item, text: normalize(item.dataset.crudSearchText || clone.textContent) };
        });
        // Server-rendered zero-row messages are replaced by one shared empty state.
        own('tbody > tr').filter(row => !row.hasAttribute('data-crud-item') && row.querySelector('td[colspan]')).forEach(row => { row.hidden = true; });
        let saved;
        try { saved = JSON.parse(sessionStorage.getItem(storageKey) || 'null'); } catch { /* optional navigation memory */ }
        if (saved) {
            if (search && !search.name && typeof saved.search === 'string') search.value = saved.search;
            filters.forEach(filter => {
                const value = saved.filters?.[filter.dataset.crudFilter];
                if (Array.from(filter.options).some(option => option.value === value)) filter.value = value;
            });
            page = Math.max(1, Number.parseInt(saved.page, 10) || 1);
        }
        const summary = document.createElement('span');
        summary.setAttribute('role', 'status');
        summary.setAttribute('aria-live', 'polite');
        const navigation = document.createElement('nav');
        navigation.className = 'crud-pagination';
        navigation.setAttribute('aria-label', `${label} pages`);
        footer.replaceChildren(summary, navigation);

        // A record marked data-crud-set-aside - a removed student, kept for their
        // history - stays out of the listing until a filter asks for exactly it,
        // so "All" means everything still in use.
        // Present and not "false": markup that writes the attribute on every row
        // with a false value must not hide the whole list.
        const setAside = record => {
            const value = record.element.getAttribute('data-crud-set-aside');
            return value !== null && normalize(value) !== 'false';
        };
        const filterMatches = (filter, record) =>
            normalize(record.element.getAttribute(`data-${filter.dataset.crudFilter}`)) === normalize(filter.value);
        const askedFor = record => filters.some(filter => filter.value && filterMatches(filter, record));

        function render() {
            const words = normalize(search?.value).split(/\s+/).filter(Boolean);
            const matches = records.filter(record => words.every(word => record.text.includes(word)) &&
                filters.every(filter => !filter.value || filterMatches(filter, record)) &&
                (!setAside(record) || askedFor(record)));
            const pages = Math.max(1, Math.ceil(matches.length / pageSize));
            // Set-aside records are not part of the total unless asked for.
            const total = records.filter(record => !setAside(record) || askedFor(record)).length;
            matchingItems = matches.map(record => record.element);
            page = Math.min(Math.max(page, 1), pages);
            items.forEach(item => { item.hidden = true; });
            // In scroll mode pageSize is Infinity, and (page - 1) * Infinity is
            // NaN, which sliced every record away and printed "Showing NaN-NaN".
            // Scrolling starts at nothing and shows the lot.
            const start = scrolls ? 0 : (page - 1) * pageSize;
            const visible = scrolls ? matches : matches.slice(start, start + pageSize);
            visible.forEach(record => { record.element.hidden = false; });
            if (empty) {
                empty.hidden = matches.length > 0;
                empty.textContent = items.length ? `No ${label} match your search or filters.` : `No ${label} yet. Use the controls above to add records when available.`;
                if (!items.length && initialEmptyMessage && !/match your search/i.test(initialEmptyMessage)) empty.textContent = initialEmptyMessage;
            }
            summary.textContent = matches.length
                ? `Showing ${start + 1}–${Math.min(start + pageSize, matches.length)} of ${matches.length} ${label}${matches.length < total ? ` (${total} total)` : ''}`
                : `Showing 0 of ${total} ${label}`;
            navigation.replaceChildren();
            if (scrolls) return;   // nothing to page through
            const addButton = (text, accessibleName, target, disabled, current = false) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-sm';
                button.textContent = text;
                button.setAttribute('aria-label', accessibleName);
                button.disabled = disabled;
                if (current) button.setAttribute('aria-current', 'page');
                button.addEventListener('click', () => {
                    page = target;
                    render();
                    navigation.querySelector('[aria-current="page"]')?.focus();
                });
                navigation.append(button);
            };
            addButton('←', `Previous ${label} page`, page - 1, page === 1);
            const first = Math.max(1, Math.min(page - 2, pages - 4));
            for (let value = first; value <= Math.min(pages, first + 4); value++) {
                addButton(String(value), `Page ${value} of ${pages}`, value, false, value === page);
            }
            addButton('→', `Next ${label} page`, page + 1, page === pages);
            try {
                sessionStorage.setItem(storageKey, JSON.stringify({ page, search: search?.value || '',
                    filters: Object.fromEntries(filters.map(filter => [filter.dataset.crudFilter, filter.value])) }));
            } catch { /* navigation works without storage */ }
        }
        search?.addEventListener('input', () => { page = 1; render(); });
        filters.forEach(filter => filter.addEventListener('change', () => { page = 1; render(); }));
        // Printing retains the full loaded report, not just the rows on screen.
        window.addEventListener('beforeprint', () => matchingItems.forEach(item => { item.hidden = false; }));
        window.addEventListener('afterprint', render);
        render();
    });
})();
