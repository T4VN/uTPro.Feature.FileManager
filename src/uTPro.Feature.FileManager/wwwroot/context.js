// Shared workspace context. Provided once at the File Manager workspace level so the
// active view (index.js) and the workspace footer app (footer.js) can coordinate:
// the footer renders New/bulk actions + item count, delegating the work to the view.

import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';

export const UTPRO_FILEMANAGER_CONTEXT = new UmbContextToken('Utpro.FileManager.Context');

const DEFAULT_FOOTER_STATE = {
    isAdmin: false,
    viewing: false,
    isEdit: false,
    isDirty: false,
    itemsLength: 0,
    totalItems: 0,
    selectedCount: 0,
    hasSelectedZips: false,
    activeSize: 0,
    activeExt: '',
};

export class UtproFileManagerContext extends UmbControllerBase {

    #footerState = new UmbObjectState(DEFAULT_FOOTER_STATE);
    /** Observable: data the footer app needs to render. */
    footerState = this.#footerState.asObservable();

    #activeView = null;

    constructor(host) {
        super(host);
        this.provideContext(UTPRO_FILEMANAGER_CONTEXT, this);
    }

    /** The mounted File Manager view registers itself here. */
    setActiveView(view) { this.#activeView = view; }
    clearActiveView(view) { if (this.#activeView === view) { this.#activeView = null; this.#footerState.setValue(DEFAULT_FOOTER_STATE); } }

    /** The view pushes its current state so the footer can mirror it. */
    setFooterState(state) { this.#footerState.setValue({ ...DEFAULT_FOOTER_STATE, ...state }); }

    // ── Actions delegated to the active view ──
    create(type) { this.#activeView?.fmCreate(type); }
    importUrl() { this.#activeView?.fmImportUrl(); }
    triggerUpload() { this.#activeView?.fmTriggerUpload(); }
    bulkAction(action) { this.#activeView?.fmBulkAction(action); }

    // ── Actions for the currently open file ──
    save() { this.#activeView?.fmSave(); }
    download() { this.#activeView?.fmDownloadActive(); }
    rename() { this.#activeView?.fmRenameActive(); }
    deleteActive() { this.#activeView?.fmDeleteActive(); }
}

export default UtproFileManagerContext;
