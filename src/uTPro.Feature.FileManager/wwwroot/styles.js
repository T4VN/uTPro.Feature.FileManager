import { css } from '@umbraco-cms/backoffice/external/lit';

export const dashboardStyles = css`
    :host { display: block; padding: 20px; }

    /* Navbar - Windows Explorer style */
    .navbar { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; padding: 6px; background: var(--uui-color-surface, #fff); border: 1px solid var(--uui-color-border, #ccc); border-radius: 6px; min-width: 0; }
    .nav-buttons { display: flex; gap: 2px; flex-shrink: 0; }
    .nav-btn { background: none; border: none; cursor: pointer; font-size: 16px; width: 30px; height: 30px; display: flex; align-items: center; justify-content: center; border-radius: 4px; color: var(--uui-color-text, #333); }
    .nav-btn:hover:not(.disabled) { background: var(--uui-color-surface-alt, #f4f4f4); }
    .nav-btn.disabled { opacity: 0.3; cursor: default; }
    .path-bar { flex: 1; min-width: 0; display: flex; align-items: center; gap: 2px; padding: 4px 10px; background: var(--uui-color-surface-alt, #f6f6f6); border: 1px solid var(--uui-color-border, #ddd); border-radius: 4px; height: 34px; box-sizing: border-box; overflow-x: auto; white-space: nowrap; scrollbar-width: none; }
    .path-bar::-webkit-scrollbar { display: none; }
    .path-crumb { cursor: pointer; padding: 2px 4px; border-radius: 3px; font-size: .9rem; color: var(--uui-color-interactive, #1b264f); }
    .path-crumb:hover { background: var(--uui-color-border, #e0e0e0); }
    .path-sep { color: #aaa; font-size: .85rem; }
    .search-input { padding: 4px 10px; border: 1px solid var(--uui-color-border, #ccc); border-radius: 4px; font-size: .9rem; width: 180px; height: 34px; box-sizing: border-box; flex-shrink: 0; background: var(--uui-color-surface, #fff); color: var(--uui-color-text, #333); }
    .search-input:focus { outline: none; border-color: var(--uui-color-interactive, #1b264f); }

    /* Action bar */
    .action-bar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; gap: 10px; flex-wrap: wrap; }
    .toolbar { display: flex; gap: 8px; align-items: center; }
    .file-status { font-size: .8rem; color: #888; white-space: nowrap; }
    .new-menu-wrap { position: relative; }
    .new-menu { position: absolute; top: 100%; left: 0; margin-top: 4px; background: var(--uui-color-surface, #fff); border: 1px solid var(--uui-color-border, #ccc); border-radius: 6px; box-shadow: 0 4px 12px rgba(0,0,0,.15); z-index: 100; min-width: 150px; overflow: hidden; }
    .new-menu-item { padding: 8px 14px; cursor: pointer; font-size: .9rem; white-space: nowrap; }
    .new-menu-item:hover { background: var(--uui-color-surface-alt, #f4f4f4); }
    .new-menu-sep { height: 1px; background: var(--uui-color-border, #ddd); margin: 4px 0; }

    /* Drop zone */
    .drop-zone { min-height: 4px; border-radius: 8px; transition: all .2s; text-align: center; color: #888; }
    .drop-zone.active { min-height: 60px; border: 2px dashed var(--uui-color-interactive, #1b264f); background: var(--uui-color-surface-alt, #f0f4ff); display: flex; align-items: center; justify-content: center; margin-bottom: 12px; }

    /* States */
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { text-align: center; padding: 40px; color: #888; font-style: italic; }
    .error { background: #fef2f2; color: #dc2626; padding: 10px 14px; border-radius: 6px; margin-bottom: 12px; font-size: .9rem; }

    /* Table */
    uui-table { width: 100%; table-layout: fixed; }
    .check-cell { width: 36px; min-width: 36px; max-width: 36px; text-align: center; }
    .selected { background: var(--uui-color-surface-alt, #f0f4ff); }
    .size-cell { width: 90px; min-width: 90px; max-width: 90px; color: #888; font-size: .85rem; text-align: right; }
    .date-cell { width: 180px; min-width: 180px; max-width: 180px; color: #888; font-size: .85rem; }
    .actions-cell { width: 150px; min-width: 150px; max-width: 150px; text-align: right; }
    .file-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; display: block; }
    .file-name.clickable { cursor: pointer; color: var(--uui-color-interactive, #1b264f); }
    .file-name.clickable:hover { text-decoration: underline; }
    .load-more { text-align: center; padding: 16px; }

    /* Editor */
    .editor-bar { display: flex; align-items: center; gap: 10px; padding: 8px 0; margin-bottom: 8px; }
    .editor-title { font-weight: 600; font-size: 1rem; }
    .editor-ext { font-size: .8rem; color: #888; font-weight: 400; background: var(--uui-color-surface-alt, #f0f0f0); padding: 2px 8px; border-radius: 4px; }
    .editor-spacer { flex: 1; }
    .dirty-badge { color: #f59e0b; font-weight: 600; font-size: .85rem; }

    /* Preview overlay */
    .overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,.6); display: flex; align-items: center; justify-content: center; z-index: 9999; }
    .preview-dialog { background: var(--uui-color-surface, #fff); border-radius: 8px; max-width: 90vw; max-height: 90vh; display: flex; flex-direction: column; box-shadow: 0 8px 32px rgba(0,0,0,.3); }
    .preview-header { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; border-bottom: 1px solid var(--uui-color-border, #ccc); }
    .preview-title { font-weight: 600; font-size: .95rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 400px; }
    .preview-body { padding: 16px; overflow: auto; display: flex; align-items: center; justify-content: center; }
    .preview-img { max-width: 80vw; max-height: 75vh; object-fit: contain; border-radius: 4px; }
    .preview-video { max-width: 80vw; max-height: 75vh; border-radius: 4px; }
    .preview-pdf { width: 80vw; height: 75vh; border: none; border-radius: 4px; }
`;
