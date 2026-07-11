import '@umbraco-cms/backoffice/code-editor';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { API_BASE, PAGE_SIZE, isMedia, isImage, isVideo, isAudio, isPdf, formatSize, getIcon, getEditorLanguage } from './helpers.js';
import { dashboardStyles } from './styles.js';
import { UTPRO_FILEMANAGER_CONTEXT } from './context.js';

export class UtproFileManagerDashboard extends UmbLitElement {
    static properties = {
        currentPath: { state: true }, items: { state: true }, parentPath: { state: true },
        isLoading: { state: true }, isLoadingMore: { state: true },
        editingFile: { state: true }, editContent: { state: true },
        errorMessage: { state: true }, dragOver: { state: true }, isDirty: { state: true },
        totalItems: { state: true }, currentPage: { state: true },
        totalPages: { state: true }, searchQuery: { state: true },
        previewFile: { state: true }, selectedPaths: { state: true },
        isAdmin: { state: true }, showNewMenu: { state: true },
        activeFile: { state: true }, showActionsMenu: { state: true },
        scanMode: { state: true }, scanFilter: { state: true }, scanData: { state: true },
    };
    static styles = dashboardStyles;

    #authContext; #fmContext = null; #originalContent = ''; #searchTimeout = null;
    #scanFiltered = []; // full list for the active scan filter (client-side paged into `items`)
    #beforeUnloadHandler = (e) => { if (this.isDirty) { e.preventDefault(); e.returnValue = ''; } };

    constructor() {
        super();
        Object.assign(this, {
            currentPath: '', items: [], parentPath: '',
            isLoading: false, isLoadingMore: false,
            editingFile: null, editContent: '',
            errorMessage: '', dragOver: false, isDirty: false,
            totalItems: 0, currentPage: 1, totalPages: 1,
            searchQuery: '', previewFile: null,
            selectedPaths: new Set(), isAdmin: false,
            showNewMenu: false, activeFile: null, showActionsMenu: false,
            scanMode: false, scanFilter: 'unused', scanData: null,
        });
        this.consumeContext(UMB_AUTH_CONTEXT, (ctx) => { this.#authContext = ctx; });
        this.consumeContext(UTPRO_FILEMANAGER_CONTEXT, (ctx) => {
            this.#fmContext = ctx;
            if (ctx) { ctx.setActiveView(this); this.#publishFooter(); }
        });
    }

    async connectedCallback() {
        super.connectedCallback();
        window.addEventListener('beforeunload', this.#beforeUnloadHandler);
        await this.#loadPermissions();
        const p = new URLSearchParams(window.location.search);
        const initialPath = p.get('path') || '';
        const pages = parseInt(p.get('pages') || '1', 10);
        const file = p.get('file') || '';
        await this.browse(initialPath);
        for (let i = 2; i <= pages; i++) await this.browse(initialPath, i, true);
        if (file) await this.#openFilePath(file);
    }
    disconnectedCallback() { super.disconnectedCallback(); window.removeEventListener('beforeunload', this.#beforeUnloadHandler); this.#fmContext?.clearActiveView(this); }

    updated() { this.#publishFooter(); }

    // ── Footer app bridge ────────────────────────────────
    // The New/bulk-action cluster and item count live in the workspace footer
    // (footer.js). We publish state here and expose action hooks it can call.

    #publishFooter() {
        const af = this.activeFile;
        this.#fmContext?.setFooterState({
            isAdmin: !!this.isAdmin,
            viewing: !!(this.editingFile || this.previewFile),
            isEdit: !!this.editingFile,
            isDirty: !!this.isDirty,
            itemsLength: this.items.length,
            totalItems: this.totalItems,
            selectedCount: this.selectedPaths.size,
            hasSelectedZips: this.#hasSelectedZips,
            activeSize: af?.size || 0,
            activeExt: af?.extension || '',
            scanMode: !!this.scanMode,
            scanFilter: this.scanFilter,
            scanCounts: this.scanData?.counts || {},
            scanThresholdMB: this.scanData?.largeThresholdMB || 0,
        });
    }

    fmCreate(type) { this.#promptCreate(type); }
    fmImportUrl() { this.#importFromUrl(); }
    fmTriggerUpload() { this.shadowRoot?.querySelector('#fileUpload')?.click(); }
    fmBulkAction(action) { this.#bulkAction(action); }
    fmSave() { this.saveFile(); }
    fmDownloadActive() { if (this.activeFile) this.downloadFile(this.activeFile); }
    fmRenameActive() { if (this.activeFile) this.renameItem(this.activeFile, true); }
    fmDeleteActive() { if (this.activeFile) this.deleteItem(this.activeFile, true); }
    fmScan() { this.runScan(); }
    fmExitScan() { this.exitScan(); }
    fmSetScanFilter(filter) { this.setScanFilter(filter); }

    // ── Media Cleanup scan mode ──────────────────────────

    async runScan() {
        if (!this.#confirmDiscard()) return;
        this.isLoading = true; this.errorMessage = '';
        this.editingFile = null; this.previewFile = null; this.activeFile = null;
        this.isDirty = false; this.selectedPaths = new Set();
        try {
            const d = await this.api('scan-media', {}, 'POST');
            this.scanData = d;
            this.scanMode = true;
            this.currentPath = '';
            this.setScanFilter('unused');
        } catch (e) { this.showError(e.message); }
        this.isLoading = false; this.#syncUrl();
    }

    exitScan() {
        this.scanMode = false; this.scanData = null; this.scanFilter = 'all';
        this.browse('');
    }

    setScanFilter(filter) {
        if (!this.scanData) return;
        const map = {
            unused: this.scanData.unused, broken: this.scanData.broken, duplicate: this.scanData.duplicate,
            orphaned: this.scanData.orphaned, large: this.scanData.large,
        };
        this.scanFilter = filter;
        this.#scanFiltered = map[filter] || [];
        this.totalItems = this.#scanFiltered.length;
        this.totalPages = Math.max(1, Math.ceil(this.totalItems / PAGE_SIZE));
        this.currentPage = 1;
        this.items = this.#scanFiltered.slice(0, PAGE_SIZE);
    }

    // ── API & helpers ────────────────────────────────────

    async api(endpoint, body = {}, method = 'POST') {
        const config = this.#authContext?.getOpenApiConfiguration();
        const headers = { 'Content-Type': 'application/json' };
        if (config?.token) { const t = await config.token(); if (t) headers['Authorization'] = `Bearer ${t}`; }
        const res = await fetch(`${API_BASE}/${endpoint}`, {
            method, headers, credentials: config?.credentials || 'same-origin',
            body: method !== 'GET' ? JSON.stringify(body) : undefined,
        });
        if (!res.ok) { const e = await res.json().catch(() => ({})); throw new Error(e.error || `HTTP ${res.status}`); }
        return res.json();
    }

    async #fetchAuth(url, opts = {}) {
        const config = this.#authContext?.getOpenApiConfiguration();
        const headers = { ...opts.headers };
        if (config?.token) { const t = await config.token(); if (t) headers['Authorization'] = `Bearer ${t}`; }
        return fetch(url, { ...opts, headers, credentials: config?.credentials || 'same-origin' });
    }

    async #loadPermissions() {
        try {
            const d = await this.api('permissions', {}, 'GET');
            this.isAdmin = d.isAdmin;
            this.hasSensitiveData = d.hasSensitiveData;
        }
        catch { this.isAdmin = false; this.hasSensitiveData = false; }
    }

    #confirmDiscard() {
        return !this.isDirty || confirm('You have unsaved changes. Discard and continue?');
    }
    showError(msg) { this.errorMessage = msg; setTimeout(() => { this.errorMessage = ''; }, 5000); }

    #syncUrl() {
        const u = new URL(window.location);
        this.currentPath ? u.searchParams.set('path', this.currentPath) : u.searchParams.delete('path');
        this.currentPage > 1 ? u.searchParams.set('pages', String(this.currentPage)) : u.searchParams.delete('pages');
        const af = this.activeFile; af ? u.searchParams.set('file', af.path) : u.searchParams.delete('file');
        window.history.replaceState(null, '', u);
    }

    async #scrollPathBar() {
        await this.updateComplete;
        setTimeout(() => { const pb = this.shadowRoot?.querySelector('.path-bar'); if (pb) pb.scrollLeft = pb.scrollWidth; }, 50);
    }

    // ── Navigation ───────────────────────────────────────

    async browse(path, page = 1, append = false) {
        if (!append && !this.#confirmDiscard()) return;
        if (append) { this.isLoadingMore = true; }
        else {
            this.isLoading = true; this.errorMessage = '';
            this.scanMode = false; this.scanData = null;
            this.editingFile = null; this.previewFile = null; this.activeFile = null;
            this.isDirty = false; this.selectedPaths = new Set();
            if (path !== this.currentPath) this.searchQuery = '';
        }
        try {
            const d = await this.api('browse', { path, page, pageSize: PAGE_SIZE, search: this.searchQuery });
            Object.assign(this, { currentPath: d.currentPath, parentPath: d.parentPath, totalItems: d.totalItems, currentPage: d.page, totalPages: d.totalPages });
            this.items = append ? [...this.items, ...d.items] : (d.items || []);
        } catch (e) { this.showError(e.message); }
        this.isLoading = false; this.isLoadingMore = false;
        this.#scrollPathBar(); this.#syncUrl();
    }

    async loadMore() {
        if (this.scanMode) {
            if (this.currentPage >= this.totalPages) return;
            this.currentPage++;
            this.items = this.#scanFiltered.slice(0, this.currentPage * PAGE_SIZE);
            return;
        }
        if (this.currentPage < this.totalPages) await this.browse(this.currentPath, this.currentPage + 1, true);
    }
    goBack() { if (this.currentPath) { const p = this.currentPath.split('/'); p.pop(); this.browse(p.join('/')); } }
    get #canGoBack() { return !!this.currentPath; }
    #handleSearch(e) { clearTimeout(this.#searchTimeout); const v = e.target.value; this.#searchTimeout = setTimeout(() => { this.searchQuery = v; this.browse(this.currentPath); }, 300); }

    // ── Open / close file ────────────────────────────────

    async openItem(item) {
        if (this.scanMode) return; // scan results are report-only
        if (item.type === 'folder') return this.browse(item.path);
        // File actions require Admin or SensitiveData
        if (!this.isAdmin && !this.hasSensitiveData) return;
        if (item.isEditable) { if (!this.#confirmDiscard()) return; return this.#openEdit(item); }
        if (isMedia(item.extension)) return this.#openPreview(item);
    }

    async #openFilePath(filePath) {
        const ext = '.' + filePath.split('.').pop().toLowerCase();
        const item = { path: filePath, name: filePath.split('/').pop(), extension: ext, type: 'file', size: 0, lastModified: new Date().toISOString() };
        isMedia(ext) ? await this.#openPreview(item) : await this.#openEdit(item);
    }

    async #openEdit(item) {
        this.previewFile = null; this.activeFile = null; this.isLoading = true;
        try {
            const d = await this.api('file-content', { path: item.path });
            this.editingFile = d; this.editContent = d.content; this.#originalContent = d.content; this.isDirty = false;
            this.activeFile = { ...item, ...d };
        } catch (e) { this.showError(e.message); }
        this.isLoading = false; this.#syncUrl(); this.#scrollPathBar();
    }

    async #openPreview(item) {
        this.editingFile = null; this.activeFile = null;
        const res = await this.#fetchAuth(`${API_BASE}/download?path=${encodeURIComponent(item.path)}&inline=true`);
        if (!res.ok) {
            const json = await res.json();
            if (json) {
                return this.showError(json.error);
            }
            return this.showError('Preview failed');
        }
        const blob = await res.blob();
        this.previewFile = { ...item, url: URL.createObjectURL(blob), type: blob.type, ext: item.extension };
        this.activeFile = item; this.#syncUrl(); this.#scrollPathBar();
    }

    closeFile() {
        if (this.editingFile && !this.#confirmDiscard()) return;
        if (this.previewFile) URL.revokeObjectURL(this.previewFile.url);
        this.editingFile = null; this.previewFile = null; this.activeFile = null; this.isDirty = false;
        this.#syncUrl();
    }

    // ── File operations (shared) ─────────────────────────

    async saveFile() {
        try {
            // Get latest code from editor in case events didn't fire
            const editor = this.shadowRoot?.querySelector('umb-code-editor');
            if (editor) this.editContent = editor.code || this.editContent;
            await this.api('save-file', { path: this.editingFile.path, content: this.editContent });
            this.#originalContent = this.editContent; this.isDirty = false;
        } catch (e) { this.showError(e.message); }
    }

    async renameItem(item, reopen = false) {
        const n = prompt('New name:', item.name);
        if (!n || n === item.name) return;
        try {
            await this.api('rename', { path: item.path, newName: n });
            if (reopen) {
                const newPath = item.path.substring(0, item.path.lastIndexOf('/') + 1) + n;
                const ext = '.' + n.split('.').pop().toLowerCase();
                const updated = { ...item, path: newPath, name: n, extension: ext };
                if (this.editingFile) await this.#openEdit(updated);
                else if (this.previewFile) await this.#openPreview(updated);
                // Refresh file list without closing the file view
                const d = await this.api('browse', { path: this.currentPath, page: 1, pageSize: PAGE_SIZE, search: this.searchQuery });
                this.items = d.items || []; this.totalItems = d.totalItems; this.totalPages = d.totalPages;
            } else {
                await this.browse(this.currentPath);
            }
        } catch (e) { this.showError(e.message); }
    }

    async deleteItem(item, closeAfter = false) {
        if (!confirm(`Delete "${item.name}"?`)) return;
        try {
            await this.api('delete', { path: item.path });
            if (closeAfter) this.closeFile();
            await this.browse(this.currentPath);
        } catch (e) { this.showError(e.message); }
    }

    async downloadFile(item) {
        const res = await this.#fetchAuth(`${API_BASE}/download?path=${encodeURIComponent(item.path)}`);
        if (!res.ok) return this.showError('Download failed');
        const blob = await res.blob(); const url = URL.createObjectURL(blob);
        const a = document.createElement('a'); a.href = url; a.download = item.name; a.click(); URL.revokeObjectURL(url);
    }

    async uploadFiles(files) {
        this.isLoading = true;
        try {
            for (const file of files) {
                const fd = new FormData(); fd.append('path', this.currentPath); fd.append('file', file);
                const config = this.#authContext?.getOpenApiConfiguration();
                const headers = {};
                if (config?.token) { const t = await config.token(); if (t) headers['Authorization'] = `Bearer ${t}`; }
                const r = await fetch(`${API_BASE}/upload`, { method: 'POST', body: fd, headers, credentials: config?.credentials || 'same-origin' });
                if (!r.ok) { const e = await r.json().catch(() => ({})); throw new Error(e.error || `Upload failed: ${file.name}`); }
            }
            await this.browse(this.currentPath);
        } catch (e) { this.showError(e.message); }
        this.isLoading = false;
    }

    async #promptCreate(type) {
        this.showNewMenu = false;
        const label = type === 'folder' ? 'New folder name:' : 'New file name (e.g. style.css):';
        const n = prompt(label); if (!n) return;
        try { await this.api(`create-${type}`, { path: this.currentPath, name: n }); await this.browse(this.currentPath); }
        catch (e) { this.showError(e.message); }
    }

    async #importFromUrl() {
        this.showNewMenu = false;
        const u = prompt('Enter file URL to import:'); if (!u) return;
        this.isLoading = true;
        try { await this.api('import-url', { path: this.currentPath, url: u }); await this.browse(this.currentPath); }
        catch (e) { this.showError(e.message); }
        this.isLoading = false;
    }

    // ── Selection ────────────────────────────────────────

    #toggleSelect(path) { const s = new Set(this.selectedPaths); s.has(path) ? s.delete(path) : s.add(path); this.selectedPaths = s; }
    #toggleSelectAll() { this.selectedPaths = this.selectedPaths.size === this.items.length ? new Set() : new Set(this.items.map(i => i.path)); }
    async #bulkAction(action) {
        const paths = [...this.selectedPaths];
        if (action === 'delete') { if (!confirm(`Delete ${paths.length} selected item(s)?`)) return; }
        else if (action === 'extract-zip') {
            const zips = paths.filter(p => p.toLowerCase().endsWith('.zip'));
            if (!zips.length || !confirm(`Extract ${zips.length} zip file(s)?`)) return;
        }
        this.isLoading = true;
        try {
            const items = action === 'extract-zip' ? paths.filter(p => p.toLowerCase().endsWith('.zip')) : paths;
            for (const path of items) await this.api(action, { path });
            this.selectedPaths = new Set(); await this.browse(this.currentPath);
        } catch (e) { this.showError(e.message); }
        this.isLoading = false;
    }
    get #hasSelectedZips() { return [...this.selectedPaths].some(p => p.toLowerCase().endsWith('.zip')); }

    // ── Render ────────────────────────────────────────────

    render() {
        const viewing = this.editingFile || this.previewFile;
        const isEdit = !!this.editingFile;
        return html`<div class="fm-root">
            <div style="position:sticky; top:0; z-index:999; background: var(--uui-color-surface, #fff); margin:-12px -16px 0; padding:0 16px;">
                ${this._renderDropZone(this.isAdmin && !this.scanMode)}
                ${this._renderNavBar()}
                ${this._renderDropZone(this.isAdmin && !this.scanMode)}
            </div>
            ${isEdit ? this._renderEditorContent() : this._renderPreviewContent()}
            ${!viewing ? html`
                ${this.errorMessage ? html`<div class="error">${this.errorMessage}</div>` : nothing}
                ${this.isLoading ? html`<div class="center"><uui-loader></uui-loader></div>` : this._renderFileList()}
            `: nothing}
            ${this.isAdmin ? html`<input type="file" id="fileUpload" multiple style="display:none" @change=${(e) => { if (e.target.files.length) { const f = Array.from(e.target.files); e.target.value = ''; this.uploadFiles(f); } }}>` : nothing}
        </div>`;
    }

    _renderNavBar() {
        const parts = this.currentPath ? this.currentPath.split('/') : [];
        const af = this.activeFile;
        return html`<div class="navbar">
            <div class="nav-buttons">
                <button class="nav-btn ${this.scanMode ? 'disabled' : (this.#canGoBack || af ? '' : 'disabled')}" ?disabled=${this.scanMode} @click=${() => { if (this.scanMode) return; af ? this.closeFile() : this.goBack(); }} title="Back"><uui-icon name="icon-arrow-left"></uui-icon></button>
                <button class="nav-btn" @click=${() => this.scanMode ? this.runScan() : window.location.reload()} title="Reload"><uui-icon name="icon-refresh"></uui-icon></button>
                <button class="nav-btn" @click=${() => { if (this.scanMode) { this.exitScan(); } else { this.closeFile(); this.browse(''); } }} title="Root"><uui-icon name="icon-home"></uui-icon></button>
            </div>
            <div class="path-bar">
                <span class="path-crumb" @click=${() => { this.closeFile(); this.browse(''); }}>root</span>
                ${parts.map((part, i) => { const p = parts.slice(0, i + 1).join('/'); return html`<span class="path-sep"><uui-symbol-expand></uui-symbol-expand></span><span class="path-crumb" @click=${() => { this.closeFile(); this.browse(p); }}>${part}</span>`; })}
                ${af ? html`<span class="path-sep"><uui-symbol-expand></uui-symbol-expand></span><span class="path-crumb path-active">${af.name}</span>` : nothing}
            </div>
            ${af ? html`<span class="file-meta" style="width:180px;text-align:right">${new Date(af.lastModified).toLocaleString()}</span>`
                : (this.scanMode ? html`<span class="file-meta scan-badge">Media Cleanup scan</span>`
                    : html`<input type="text" class="search-input" placeholder="Search..." .value=${this.searchQuery} @input=${(e) => this.#handleSearch(e)}>`)}
        </div>`;
    }

    _renderEditorContent() {
        const lang = getEditorLanguage(this.editingFile.extension);
        const onCodeChange = (e) => {
            this.editContent = e.target.code || '';
            this.isDirty = this.editContent !== this.#originalContent;
        };
        return html`<umb-code-editor .code=${this.editContent} language=${lang}
            style="--editor-height: calc(100dvh - 260px)"
            @input=${onCodeChange} @change=${onCodeChange}
        ></umb-code-editor>`;
    }

    _renderPreviewContent() {
        const f = this.previewFile; if (!f) return nothing;
        if (isImage(f.ext)) return html`<div class="preview-inline"><img class="preview-img" src=${f.url} alt=${f.name}></div>`;
        if (isVideo(f.ext)) return html`<div class="preview-inline"><video class="preview-video" controls autoplay><source src=${f.url} type=${f.type}></video></div>`;
        if (isAudio(f.ext)) return html`<div class="preview-inline"><audio controls autoplay style="width:100%"><source src=${f.url} type=${f.type}></audio></div>`;
        if (isPdf(f.ext)) return html`<div class="preview-inline"><iframe class="preview-pdf" src=${f.url}></iframe></div>`;
        return html`<div class="preview-inline"><p>Cannot preview this file type.</p></div>`;
    }

    _renderDropZone(isDrop) {
        if (isDrop) {
            return html`<div class="drop-zone ${this.dragOver ? 'active' : ''}" @dragover=${(e) => { e.preventDefault(); this.dragOver = true; }} @dragleave=${() => { this.dragOver = false; }} @drop=${(e) => { e.preventDefault(); this.dragOver = false; if (e.dataTransfer.files.length) this.uploadFiles(e.dataTransfer.files); }}>${this.dragOver ? 'Drop files here to upload' : ''}</div>`;
        }
        return html`<div class="drop-zone"></div>`;
    }

    _renderFileList() {
        if (!this.items.length) return html`<div class="empty">${this.scanMode ? 'No items found in this category.' : 'This folder is empty'}</div>`;
        return html`
            <uui-table aria-label="Files">
                <uui-table-head>
                    ${this.isAdmin && !this.scanMode ? html`<uui-table-head-cell class="check-cell"><input type="checkbox" .checked=${this.selectedPaths.size === this.items.length && this.items.length > 0} @change=${() => this.#toggleSelectAll()}></uui-table-head-cell>` : nothing}
                    <uui-table-head-cell>Name</uui-table-head-cell>
                    ${this.scanMode ? html`<uui-table-head-cell class="date-cell">Status</uui-table-head-cell>` : nothing}
                    <uui-table-head-cell class="size-cell">Size</uui-table-head-cell>
                    <uui-table-head-cell class="date-cell">Date Modified</uui-table-head-cell>
                    ${!this.scanMode ? html`<uui-table-head-cell class="actions-cell">Actions</uui-table-head-cell>` : nothing}
                </uui-table-head>
                ${this.items.map(item => this._renderRow(item))}
            </uui-table>
            ${this.currentPage < this.totalPages ? html`<div class="load-more">${this.isLoadingMore ? html`<uui-loader></uui-loader>` : html`<uui-button look="primary" @click=${() => this.loadMore()}>Load more (${this.totalItems - this.items.length} remaining)</uui-button>`}</div>` : nothing}`;
    }

    _renderRow(item) {
        if (this.scanMode) return this._renderScanRow(item);
        const canOpen = this.isAdmin || this.hasSensitiveData;
        const clickable = item.type === 'folder' || (canOpen && (item.isEditable || isMedia(item.extension)));
        return html`<uui-table-row class="${this.selectedPaths.has(item.path) ? 'selected' : ''}">
            ${this.isAdmin ? html`<uui-table-cell class="check-cell"><input type="checkbox" .checked=${this.selectedPaths.has(item.path)} @change=${() => this.#toggleSelect(item.path)}></uui-table-cell>` : nothing}
            <uui-table-cell><span class="file-name ${clickable ? 'clickable' : ''}" @click=${() => this.openItem(item)}>${getIcon(item)} ${item.name}</span></uui-table-cell>
            <uui-table-cell class="size-cell">${formatSize(item.size)}</uui-table-cell>
            <uui-table-cell class="date-cell">${new Date(item.lastModified).toLocaleString()}</uui-table-cell>
            <uui-table-cell class="actions-cell">
                ${item.type === 'file' && canOpen ? html`<uui-button look="outline" compact @click=${() => this.downloadFile(item)} title="Download"><uui-icon name="icon-download-alt"></uui-icon></uui-button>` : nothing}
                ${this.isAdmin ? html`<uui-button look="outline" compact @click=${() => this.renameItem(item)} title="Rename"><uui-icon name="icon-edit"></uui-icon></uui-button>
                <uui-button look="outline" compact @click=${() => this.deleteItem(item)} color="danger" title="Delete"><uui-icon name="icon-trash"></uui-icon></uui-button>` : nothing}
            </uui-table-cell>
        </uui-table-row>`;
    }

    _renderScanRow(item) {
        const badge = item.detail || item.category || '';
        const modified = item.lastModified && new Date(item.lastModified).getFullYear() > 1
            ? new Date(item.lastModified).toLocaleString() : '';
        return html`<uui-table-row>
            <uui-table-cell><span class="file-name" title=${item.path}>${getIcon(item)} ${item.name}</span></uui-table-cell>
            <uui-table-cell class="date-cell">${badge ? html`<span class="scan-tag scan-tag-${item.category}">${badge}</span>` : nothing}</uui-table-cell>
            <uui-table-cell class="size-cell">${formatSize(item.size)}</uui-table-cell>
            <uui-table-cell class="date-cell">${modified}</uui-table-cell>
        </uui-table-row>`;
    }
}
customElements.define('utpro-file-manager-dashboard', UtproFileManagerDashboard);
export default UtproFileManagerDashboard;
