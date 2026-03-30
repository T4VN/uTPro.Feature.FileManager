import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { API_BASE, PAGE_SIZE, isMedia, isImage, isVideo, isAudio, isPdf, formatSize, getIcon, getEditorLanguage } from './helpers.js';
import { dashboardStyles } from './styles.js';

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
        activeFile: { state: true },
    };
    static styles = dashboardStyles;

    #authContext; #originalContent = ''; #searchTimeout = null;
    #beforeUnloadHandler = (e) => { if (this.isDirty) { e.preventDefault(); e.returnValue = ''; } };

    constructor() {
        super();
        this.currentPath = ''; this.items = []; this.parentPath = '';
        this.isLoading = false; this.isLoadingMore = false;
        this.editingFile = null; this.editContent = '';
        this.errorMessage = ''; this.dragOver = false; this.isDirty = false;
        this.totalItems = 0; this.currentPage = 1; this.totalPages = 1;
        this.searchQuery = ''; this.previewFile = null;
        this.selectedPaths = new Set(); this.isAdmin = false;
        this.showNewMenu = false; this.activeFile = null;
        this.consumeContext(UMB_AUTH_CONTEXT, (ctx) => { this.#authContext = ctx; });
    }

    async connectedCallback() {
        super.connectedCallback();
        window.addEventListener('beforeunload', this.#beforeUnloadHandler);
        await this.#loadPermissions();
        const params = new URLSearchParams(window.location.search);
        const file = params.get('file') || '';
        const initialPath = params.get('path') || '';
        const initialPages = parseInt(params.get('pages') || '1', 10);
        await this.browse(initialPath);
        for (let p = 2; p <= initialPages; p++) await this.browse(initialPath, p, true);
        if (file) await this.#openFilePath(file);
    }

    disconnectedCallback() { super.disconnectedCallback(); window.removeEventListener('beforeunload', this.#beforeUnloadHandler); }

    // ── API ──────────────────────────────────────────────
    async api(endpoint, body = {}, method = 'POST') {
        const config = this.#authContext?.getOpenApiConfiguration();
        const headers = { 'Content-Type': 'application/json' };
        if (config?.token) { const token = await config.token(); if (token) headers['Authorization'] = `Bearer ${token}`; }
        const res = await fetch(`${API_BASE}/${endpoint}`, { method, headers, credentials: config?.credentials || 'same-origin', body: method !== 'GET' ? JSON.stringify(body) : undefined });
        if (!res.ok) { const err = await res.json().catch(() => ({})); throw new Error(err.error || `HTTP ${res.status}`); }
        return res.json();
    }
    async #getAuthHeaders() {
        const config = this.#authContext?.getOpenApiConfiguration();
        const headers = {};
        if (config?.token) { const token = await config.token(); if (token) headers['Authorization'] = `Bearer ${token}`; }
        return { headers, credentials: config?.credentials || 'same-origin' };
    }
    async #loadPermissions() {
        try { const d = await this.api('permissions', {}, 'GET'); this.isAdmin = d.isAdmin; }
        catch { this.isAdmin = false; }
    }
    #confirmDiscard() { return !this.isDirty || confirm('You have unsaved changes. Discard and continue?'); }
    showError(msg) { this.errorMessage = msg; setTimeout(() => { this.errorMessage = ''; }, 5000); }

    // ── URL sync ─────────────────────────────────────────
    #syncUrl() {
        const url = new URL(window.location);
        if (this.currentPath) url.searchParams.set('path', this.currentPath); else url.searchParams.delete('path');
        if (this.currentPage > 1) url.searchParams.set('pages', String(this.currentPage)); else url.searchParams.delete('pages');
        const af = this.activeFile || this.editingFile;
        if (af) url.searchParams.set('file', af.path || af.name); else url.searchParams.delete('file');
        window.history.replaceState(null, '', url);
    }

    // ── Browse ───────────────────────────────────────────
    async browse(path, page = 1, append = false) {
        if (!append && !this.#confirmDiscard()) return;
        if (append) { this.isLoadingMore = true; }
        else {
            this.isLoading = true; this.errorMessage = '';
            this.editingFile = null; this.previewFile = null; this.activeFile = null;
            this.isDirty = false; this.selectedPaths = new Set();
            if (path !== this.currentPath) this.searchQuery = '';
        }
        try {
            const data = await this.api('browse', { path, page, pageSize: PAGE_SIZE, search: this.searchQuery });
            this.currentPath = data.currentPath; this.parentPath = data.parentPath;
            this.totalItems = data.totalItems; this.currentPage = data.page; this.totalPages = data.totalPages;
            this.items = append ? [...this.items, ...data.items] : (data.items || []);
        } catch (e) { this.showError(e.message); }
        this.isLoading = false; this.isLoadingMore = false;
        this.#scrollPathBar();
        this.#syncUrl();
    }
    async loadMore() { if (this.currentPage < this.totalPages) await this.browse(this.currentPath, this.currentPage + 1, true); }
    #handleSearch(e) { clearTimeout(this.#searchTimeout); const v = e.target.value; this.#searchTimeout = setTimeout(() => { this.searchQuery = v; this.browse(this.currentPath); }, 300); }
    async #scrollPathBar() {
        await this.updateComplete;
        setTimeout(() => { const pb = this.shadowRoot?.querySelector('.path-bar'); if (pb) pb.scrollLeft = pb.scrollWidth; }, 50);
    }
    goBack() { if (this.currentPath) { const parts = this.currentPath.split('/'); parts.pop(); this.browse(parts.join('/')); } }
    get #canGoBack() { return this.currentPath !== ''; }

    // ── Open file (edit or preview) ──────────────────────
    async openItem(item) {
        if (item.type === 'folder') return this.browse(item.path);
        if (item.isEditable) { if (!this.#confirmDiscard()) return; return this.#openEdit(item); }
        if (isMedia(item.extension)) return this.#openPreview(item);
    }
    async #openFilePath(filePath) {
        // Determine if editable or media from extension
        const ext = '.' + filePath.split('.').pop().toLowerCase();
        const item = { path: filePath, name: filePath.split('/').pop(), extension: ext, type: 'file', size: 0, lastModified: new Date().toISOString() };
        if (isMedia(ext)) await this.#openPreview(item);
        else await this.#openEdit(item);
    }
    async #openEdit(item) {
        this.previewFile = null; this.activeFile = null;
        this.isLoading = true;
        try {
            const data = await this.api('file-content', { path: item.path });
            this.editingFile = data; this.editContent = data.content;
            this.#originalContent = data.content; this.isDirty = false;
            this.activeFile = { ...item, ...data };
        } catch (e) { this.showError(e.message); }
        this.isLoading = false;
        this.#syncUrl();
        this.#scrollPathBar();
    }
    async #openPreview(item) {
        this.editingFile = null; this.activeFile = null;
        const { headers, credentials } = await this.#getAuthHeaders();
        const res = await fetch(`${API_BASE}/download?path=${encodeURIComponent(item.path)}&inline=true`, { headers, credentials });
        if (!res.ok) return this.showError('Preview failed');
        const blob = await res.blob();
        this.previewFile = { ...item, url: URL.createObjectURL(blob), type: blob.type, ext: item.extension };
        this.activeFile = item;
        this.#syncUrl();
        this.#scrollPathBar();
    }
    closeFile() {
        if (this.editingFile && !this.#confirmDiscard()) return;
        if (this.previewFile) URL.revokeObjectURL(this.previewFile.url);
        this.editingFile = null; this.previewFile = null; this.activeFile = null; this.isDirty = false;
        this.#syncUrl();
    }

    // ── File operations ──────────────────────────────────
    async saveFile() {
        try {
            await this.api('save-file', { path: this.editingFile.path, content: this.editContent });
            this.#originalContent = this.editContent; this.isDirty = false;
        } catch (e) { this.showError(e.message); }
    }
    async createFolder() { this.showNewMenu = false; const n = prompt('New folder name:'); if (!n) return; try { await this.api('create-folder', { path: this.currentPath, name: n }); await this.browse(this.currentPath); } catch (e) { this.showError(e.message); } }
    async createFile() { this.showNewMenu = false; const n = prompt('New file name (e.g. style.css):'); if (!n) return; try { await this.api('create-file', { path: this.currentPath, name: n }); await this.browse(this.currentPath); } catch (e) { this.showError(e.message); } }
    async importFromUrl() { this.showNewMenu = false; const u = prompt('Enter file URL to import:'); if (!u) return; this.isLoading = true; try { await this.api('import-url', { path: this.currentPath, url: u }); await this.browse(this.currentPath); } catch (e) { this.showError(e.message); } this.isLoading = false; }
    async #renameActiveFile() {
        const af = this.activeFile; if (!af) return;
        const newName = prompt('New name:', af.name); if (!newName || newName === af.name) return;
        try {
            await this.api('rename', { path: af.path, newName });
            const newPath = af.path.substring(0, af.path.lastIndexOf('/') + 1) + newName;
            const ext = '.' + newName.split('.').pop().toLowerCase();
            // Reload file with new name
            if (this.editingFile) await this.#openEdit({ ...af, path: newPath, name: newName, extension: ext });
            else if (this.previewFile) await this.#openPreview({ ...af, path: newPath, name: newName, extension: ext });
            await this.browse(this.currentPath);
        } catch (e) { this.showError(e.message); }
    }
    async #deleteActiveFile() {
        const af = this.activeFile; if (!af) return;
        if (!confirm(`Delete "${af.name}"?`)) return;
        try { await this.api('delete', { path: af.path }); this.closeFile(); await this.browse(this.currentPath); }
        catch (e) { this.showError(e.message); }
    }
    async downloadFile(item) {
        const { headers, credentials } = await this.#getAuthHeaders();
        const res = await fetch(`${API_BASE}/download?path=${encodeURIComponent(item.path)}`, { headers, credentials });
        if (!res.ok) return this.showError('Download failed');
        const blob = await res.blob(); const url = URL.createObjectURL(blob);
        const a = document.createElement('a'); a.href = url; a.download = item.name; a.click(); URL.revokeObjectURL(url);
    }
    async renameItem(item) { const n = prompt('New name:', item.name); if (!n || n === item.name) return; try { await this.api('rename', { path: item.path, newName: n }); await this.browse(this.currentPath); } catch (e) { this.showError(e.message); } }
    async deleteItem(item) { if (!confirm(`Delete "${item.name}"?`)) return; try { await this.api('delete', { path: item.path }); await this.browse(this.currentPath); } catch (e) { this.showError(e.message); } }
    async uploadFiles(files) {
        this.isLoading = true;
        try {
            const { headers, credentials } = await this.#getAuthHeaders();
            const fh = {}; if (headers['Authorization']) fh['Authorization'] = headers['Authorization'];
            for (const file of files) { const fd = new FormData(); fd.append('path', this.currentPath); fd.append('file', file); const r = await fetch(`${API_BASE}/upload`, { method: 'POST', body: fd, headers: fh, credentials }); if (!r.ok) { const e = await r.json().catch(() => ({})); throw new Error(e.error || `Upload failed: ${file.name}`); } }
            await this.browse(this.currentPath);
        } catch (e) { this.showError(e.message); } this.isLoading = false;
    }

    // ── Selection ────────────────────────────────────────
    #toggleSelect(path) { const s = new Set(this.selectedPaths); if (s.has(path)) s.delete(path); else s.add(path); this.selectedPaths = s; }
    #toggleSelectAll() { this.selectedPaths = this.selectedPaths.size === this.items.length ? new Set() : new Set(this.items.map(i => i.path)); }
    async #deleteSelected() { const c = this.selectedPaths.size; if (!c || !confirm(`Delete ${c} selected item(s)?`)) return; this.isLoading = true; try { for (const p of this.selectedPaths) await this.api('delete', { path: p }); this.selectedPaths = new Set(); await this.browse(this.currentPath); } catch (e) { this.showError(e.message); } this.isLoading = false; }
    async #extractSelected() { const z = [...this.selectedPaths].filter(p => p.toLowerCase().endsWith('.zip')); if (!z.length) return; if (!confirm(`Extract ${z.length} zip file(s)?`)) return; this.isLoading = true; try { for (const p of z) await this.api('extract-zip', { path: p }); this.selectedPaths = new Set(); await this.browse(this.currentPath); } catch (e) { this.showError(e.message); } this.isLoading = false; }
    get #hasSelectedZips() { return [...this.selectedPaths].some(p => p.toLowerCase().endsWith('.zip')); }

    // ── Render ────────────────────────────────────────────
    render() {
        const viewing = this.editingFile || this.previewFile;
        return html`
            <uui-box>
                ${this._renderNavBar()}
                ${viewing ? this._renderFileView() : html`
                    ${this._renderActionBar()}
                    ${this.errorMessage ? html`<div class="error">${this.errorMessage}</div>` : nothing}
                    ${this.isAdmin ? this._renderDropZone() : nothing}
                    ${this.isLoading ? html`<div class="center"><uui-loader></uui-loader></div>` : this._renderFileList()}
                `}
            </uui-box>`;
    }

    _renderNavBar() {
        const parts = this.currentPath ? this.currentPath.split('/') : [];
        const af = this.activeFile;
        return html`
            <div class="navbar">
                <div class="nav-buttons">
                    <button class="nav-btn ${this.#canGoBack || af ? '' : 'disabled'}" @click=${() => af ? this.closeFile() : this.goBack()} title="Back">\u2190</button>
                    <button class="nav-btn" @click=${() => window.location.reload()} title="Reload">\u21BB</button>
                </div>
                <div class="path-bar">
                    <span class="path-crumb" @click=${() => { this.closeFile(); this.browse(''); }}>\u{1F3E0}</span>
                    ${parts.map((part, i) => { const p = parts.slice(0, i + 1).join('/'); return html`<span class="path-sep">\u203A</span><span class="path-crumb" @click=${() => { this.closeFile(); this.browse(p); }}>${part}</span>`; })}
                    ${af ? html`<span class="path-sep">\u203A</span><span class="path-crumb path-active">${af.name}</span>` : nothing}
                </div>
                <input type="text" class="search-input" placeholder="Search..." .value=${this.searchQuery} @input=${(e) => this.#handleSearch(e)}>
            </div>`;
    }

    _renderFileView() {
        const af = this.activeFile;
        const isEdit = !!this.editingFile;
        const pf = this.previewFile;
        return html`
            <div class="file-view-bar">
                <div class="file-view-actions">
                    ${isEdit ? html`<uui-button look="outline" compact @click=${() => this.saveFile()} title="Save">\u{1F4BE} Save</uui-button>` : nothing}
                    ${!isEdit ? html`
                        <div class="new-menu-wrap">
                            <uui-button look="outline" compact @click=${() => { this._showActionsMenu = !this._showActionsMenu; this.requestUpdate(); }}>Actions \u25BE</uui-button>
                            ${this._showActionsMenu ? html`<div class="new-menu">
                                <div class="new-menu-item" @click=${() => { this._showActionsMenu = false; this.downloadFile(af); }}>\u2B07\uFE0F Download</div>
                                ${this.isAdmin ? html`
                                    <div class="new-menu-item" @click=${() => { this._showActionsMenu = false; this.#renameActiveFile(); }}>\u270F\uFE0F Rename</div>
                                    <div class="new-menu-sep"></div>
                                    <div class="new-menu-item new-menu-danger" @click=${() => { this._showActionsMenu = false; this.#deleteActiveFile(); }}>\u{1F5D1}\uFE0F Delete</div>
                                ` : nothing}
                            </div>` : nothing}
                        </div>
                    ` : html`
                        <uui-button look="outline" compact @click=${() => this.downloadFile(af)} title="Download">\u2B07\uFE0F</uui-button>
                        ${this.isAdmin ? html`
                            <uui-button look="outline" compact @click=${() => this.#renameActiveFile()} title="Rename">\u270F\uFE0F</uui-button>
                            <uui-button look="outline" compact color="danger" @click=${() => this.#deleteActiveFile()} title="Delete">\u{1F5D1}\uFE0F</uui-button>
                        ` : nothing}
                    `}
                </div>
                <div class="file-view-info">
                    ${isEdit && this.isDirty ? html`<span class="dirty-badge">\u25CF Unsaved</span>` : nothing}
                    ${af?.size ? html`<span class="file-meta">${formatSize(af.size)}</span>` : nothing}
                    ${af?.lastModified ? html`<span class="file-meta">${new Date(af.lastModified).toLocaleString()}</span>` : nothing}
                    <span class="editor-ext">${af?.extension || ''}</span>
                </div>
            </div>
            ${isEdit ? this._renderEditorContent() : this._renderPreviewContent(pf)}`;
    }

    _renderEditorContent() {
        const lang = getEditorLanguage(this.editingFile.extension);
        return html`<umb-code-editor
            .code=${this.editContent}
            language=${lang}
            style="--editor-height: calc(100dvh - 260px)"
            @input=${(e) => { this.editContent = e.target.code || ''; this.isDirty = this.editContent !== this.#originalContent; }}
        ></umb-code-editor>`;
    }

    _renderPreviewContent(f) {
        if (!f) return nothing;
        let content;
        if (isImage(f.ext)) content = html`<img class="preview-img" src=${f.url} alt=${f.name}>`;
        else if (isVideo(f.ext)) content = html`<video class="preview-video" controls autoplay><source src=${f.url} type=${f.type}></video>`;
        else if (isAudio(f.ext)) content = html`<audio controls autoplay style="width:100%"><source src=${f.url} type=${f.type}></audio>`;
        else if (isPdf(f.ext)) content = html`<iframe class="preview-pdf" src=${f.url}></iframe>`;
        else content = html`<p>Cannot preview this file type.</p>`;
        return html`<div class="preview-inline">${content}</div>`;
    }

    _renderActionBar() {
        return html`
            <div class="action-bar">
                <div class="toolbar">
                    ${this.isAdmin ? html`
                        <div class="new-menu-wrap">
                            <uui-button look="outline" @click=${() => { this.showNewMenu = !this.showNewMenu; }}>\u2795 New \u25BE</uui-button>
                            ${this.showNewMenu ? html`<div class="new-menu">
                                <div class="new-menu-item" @click=${() => { this.showNewMenu = false; this.shadowRoot.querySelector('#fileUpload').click(); }}>\u{1F4E4} Upload File</div>
                                <div class="new-menu-sep"></div>
                                <div class="new-menu-item" @click=${() => this.createFolder()}>\u{1F4C1} New Folder</div>
                                <div class="new-menu-item" @click=${() => this.createFile()}>\u{1F4C4} New File</div>
                                <div class="new-menu-sep"></div>
                                <div class="new-menu-item" @click=${() => this.importFromUrl()}>\u{1F310} Import file via URL</div>
                            </div>` : nothing}
                        </div>
                        <input type="file" id="fileUpload" multiple style="display:none" @change=${(e) => { if (e.target.files.length) { const f = Array.from(e.target.files); e.target.value = ''; this.uploadFiles(f); } }}>
                    ` : nothing}
                    ${this.isAdmin && this.selectedPaths.size ? html`<uui-button look="primary" color="danger" @click=${() => this.#deleteSelected()}>\u{1F5D1}\uFE0F Delete (${this.selectedPaths.size})</uui-button>` : nothing}
                    ${this.isAdmin && this.#hasSelectedZips ? html`<uui-button look="primary" @click=${() => this.#extractSelected()}>\u{1F4E6} Extract Zip</uui-button>` : nothing}
                </div>
                ${this.totalItems ? html`<div class="file-status">Showing ${this.items.length} of ${this.totalItems} items</div>` : nothing}
            </div>`;
    }

    _renderDropZone() {
        return html`<div class="drop-zone ${this.dragOver ? 'active' : ''}" @dragover=${(e) => { e.preventDefault(); this.dragOver = true; }} @dragleave=${() => { this.dragOver = false; }} @drop=${(e) => { e.preventDefault(); this.dragOver = false; if (e.dataTransfer.files.length) this.uploadFiles(e.dataTransfer.files); }}>${this.dragOver ? 'Drop files here to upload' : ''}</div>`;
    }

    _renderFileList() {
        if (!this.items.length) return html`<div class="empty">This folder is empty</div>`;
        const hasMore = this.currentPage < this.totalPages;
        return html`
            <uui-table aria-label="Files">
                <uui-table-head>
                    ${this.isAdmin ? html`<uui-table-head-cell class="check-cell"><input type="checkbox" .checked=${this.selectedPaths.size === this.items.length && this.items.length > 0} @change=${() => this.#toggleSelectAll()}></uui-table-head-cell>` : nothing}
                    <uui-table-head-cell>Name</uui-table-head-cell>
                    <uui-table-head-cell class="size-cell">Size</uui-table-head-cell>
                    <uui-table-head-cell class="date-cell">Date Modified</uui-table-head-cell>
                    <uui-table-head-cell class="actions-cell">Actions</uui-table-head-cell>
                </uui-table-head>
                ${this.items.map(item => this._renderRow(item))}
            </uui-table>
            ${hasMore ? html`<div class="load-more">${this.isLoadingMore ? html`<uui-loader></uui-loader>` : html`<uui-button look="primary" @click=${() => this.loadMore()}>Load more (${this.totalItems - this.items.length} remaining)</uui-button>`}</div>` : nothing}`;
    }

    _renderRow(item) {
        const clickable = item.type === 'folder' || item.isEditable || isMedia(item.extension);
        return html`
            <uui-table-row class="${this.selectedPaths.has(item.path) ? 'selected' : ''}">
                ${this.isAdmin ? html`<uui-table-cell class="check-cell"><input type="checkbox" .checked=${this.selectedPaths.has(item.path)} @change=${() => this.#toggleSelect(item.path)}></uui-table-cell>` : nothing}
                <uui-table-cell><span class="file-name ${clickable ? 'clickable' : ''}" @click=${() => this.openItem(item)}>${getIcon(item)} ${item.name}</span></uui-table-cell>
                <uui-table-cell class="size-cell">${formatSize(item.size)}</uui-table-cell>
                <uui-table-cell class="date-cell">${new Date(item.lastModified).toLocaleString()}</uui-table-cell>
                <uui-table-cell class="actions-cell">
                    ${item.type === 'file' ? html`<uui-button look="outline" compact @click=${() => this.downloadFile(item)} title="Download">\u2B07\uFE0F</uui-button>` : nothing}
                    ${this.isAdmin ? html`<uui-button look="outline" compact @click=${() => this.renameItem(item)} title="Rename">\u270F\uFE0F</uui-button><uui-button look="outline" compact @click=${() => this.deleteItem(item)} color="danger" title="Delete">\u{1F5D1}\uFE0F</uui-button>` : nothing}
                </uui-table-cell>
            </uui-table-row>`;
    }
}
customElements.define('utpro-file-manager-dashboard', UtproFileManagerDashboard);
export default UtproFileManagerDashboard;
