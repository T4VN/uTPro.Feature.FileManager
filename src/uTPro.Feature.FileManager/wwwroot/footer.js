// Workspace footer app: renders the New menu, bulk actions and item count in the
// Umbraco workspace footer bar, driven by the shared File Manager context.

import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, css, nothing } from '@umbraco-cms/backoffice/external/lit';
import { formatSize } from './helpers.js';
import { UTPRO_FILEMANAGER_CONTEXT } from './context.js';

export class UtproFileManagerFooter extends UmbLitElement {

    static properties = {
        _s: { state: true },
        _showNewMenu: { state: true },
        _showActionsMenu: { state: true },
    };

    #context;

    constructor() {
        super();
        this._s = { isAdmin: false, viewing: false, isEdit: false, isDirty: false, itemsLength: 0, totalItems: 0, selectedCount: 0, hasSelectedZips: false, activeSize: 0, activeExt: '' };
        this._showNewMenu = false;
        this._showActionsMenu = false;
        this.consumeContext(UTPRO_FILEMANAGER_CONTEXT, (ctx) => {
            this.#context = ctx;
            if (ctx) this.observe(ctx.footerState, (v) => { if (v) this._s = v; });
        });
    }

    #toggleNew() { this._showNewMenu = !this._showNewMenu; }
    #toggleActions() { this._showActionsMenu = !this._showActionsMenu; }
    #run(fn) { this._showNewMenu = false; fn?.(); }
    #runA(fn) { this._showActionsMenu = false; fn?.(); }

    render() {
        const s = this._s;
        // When a file is open, show its Save/Actions cluster; otherwise the New/bulk cluster.
        return s.viewing ? this.#renderFileActions(s) : this.#renderListActions(s);
    }

    #renderFileActions(s) {
        return html`<div class="footer">
            <div class="left">
                ${s.isEdit ? html`<uui-button look="outline" compact @click=${() => this.#context?.save()} title="Save"><uui-icon name="icon-save"></uui-icon> Save</uui-button>` : nothing}
                <div class="new-menu-wrap">
                    <uui-button look="outline" compact @click=${() => this.#toggleActions()}><uui-icon name="icon-list"></uui-icon> Actions <uui-symbol-expand ?open=${this._showActionsMenu}></uui-symbol-expand></uui-button>
                    ${this._showActionsMenu ? html`<div class="new-menu">
                        <div class="new-menu-item" @click=${() => this.#runA(() => this.#context?.download())}><uui-icon name="icon-download-alt"></uui-icon> Download</div>
                        ${s.isAdmin ? html`
                            <div class="new-menu-item" @click=${() => this.#runA(() => this.#context?.rename())}><uui-icon name="icon-edit"></uui-icon> Rename</div>
                            <div class="new-menu-sep"></div>
                            <div class="new-menu-item new-menu-danger" @click=${() => this.#runA(() => this.#context?.deleteActive())}><uui-icon name="icon-trash"></uui-icon> Delete</div>
                        ` : nothing}
                    </div>` : nothing}
                </div>
                ${s.isEdit && s.isDirty ? html`<span class="dirty-badge"><uui-icon name="icon-lightbulb-active"></uui-icon> Unsaved</span>` : nothing}
            </div>
            <div class="right">
                ${s.activeSize ? html`<span class="file-meta">${formatSize(s.activeSize)}</span>` : nothing}
                ${s.activeExt ? html`<span class="editor-ext">${s.activeExt}</span>` : nothing}
            </div>
        </div>`;
    }

    #renderListActions(s) {
        return html`<div class="footer">
            <div class="left">
                ${s.isAdmin ? html`
                    <div class="new-menu-wrap">
                        <uui-button look="outline" compact @click=${() => this.#toggleNew()}><uui-icon name="icon-add"></uui-icon> New <uui-symbol-expand ?open=${this._showNewMenu}></uui-symbol-expand></uui-button>
                        ${this._showNewMenu ? html`<div class="new-menu">
                            <div class="new-menu-item" @click=${() => this.#run(() => this.#context?.triggerUpload())}><uui-icon name="icon-cloud-upload"></uui-icon> Upload File</div>
                            <div class="new-menu-sep"></div>
                            <div class="new-menu-item" @click=${() => this.#run(() => this.#context?.create('folder'))}><uui-icon name="icon-folder"></uui-icon> New Folder</div>
                            <div class="new-menu-item" @click=${() => this.#run(() => this.#context?.create('file'))}><uui-icon name="icon-document"></uui-icon> New File</div>
                            <div class="new-menu-sep"></div>
                            <div class="new-menu-item" @click=${() => this.#run(() => this.#context?.importUrl())}><uui-icon name="icon-globe"></uui-icon> Import file via URL</div>
                        </div>` : nothing}
                    </div>
                    ${s.selectedCount ? html`<uui-button look="primary" color="danger" compact @click=${() => this.#context?.bulkAction('delete')}><uui-icon name="icon-trash"></uui-icon> Delete (${s.selectedCount})</uui-button>` : nothing}
                    ${s.hasSelectedZips ? html`<uui-button look="primary" compact @click=${() => this.#context?.bulkAction('extract-zip')}><uui-icon name="icon-zip"></uui-icon> Extract Zip</uui-button>` : nothing}
                ` : nothing}
            </div>
            ${s.totalItems ? html`<div class="file-status">Showing ${s.itemsLength} of ${s.totalItems} items</div>` : nothing}
        </div>`;
    }

    static styles = css`
        :host { display: flex; flex: 1; }
        .footer { display: flex; flex: 1; align-items: center; justify-content: space-between; box-sizing: border-box; padding-left: var(--uui-size-layout-1); gap: var(--uui-size-space-3); }
        .left { display: flex; align-items: center; gap: var(--uui-size-space-2); }
        .right { display: flex; align-items: center; gap: 10px; }
        .file-status { font-size: .8rem; color: #888; white-space: nowrap; }
        .file-meta { font-size: .8rem; color: #999; }
        .editor-ext { font-size: .8rem; color: #888; font-weight: 400; background: var(--uui-color-surface-alt, #f0f0f0); padding: 2px 8px; border-radius: 4px; }
        .dirty-badge { color: #f59e0b; font-weight: 600; font-size: .85rem; display: flex; align-items: center; gap: 4px; }
        .new-menu-wrap { position: relative; }
        .new-menu { position: absolute; bottom: 100%; left: 0; margin-bottom: 4px; background: var(--uui-color-surface, #fff); border: 1px solid var(--uui-color-border, #ccc); border-radius: 6px; box-shadow: 0 -4px 12px rgba(0,0,0,.15); z-index: 100; min-width: 180px; overflow: hidden; }
        .new-menu-item { padding: 8px 14px; cursor: pointer; font-size: .9rem; white-space: nowrap; display: flex; align-items: center; gap: 6px; }
        .new-menu-item:hover { background: var(--uui-color-surface-alt, #f4f4f4); }
        .new-menu-danger { color: #dc2626; font-weight: 600; }
        .new-menu-danger:hover { background: #fef2f2; }
        .new-menu-sep { height: 1px; background: var(--uui-color-border, #ddd); margin: 4px 0; }
    `;
}

customElements.define('utpro-file-manager-footer', UtproFileManagerFooter);
export default UtproFileManagerFooter;
