import { html } from '@umbraco-cms/backoffice/external/lit';
export const API_BASE = '/umbraco/management/api/v1/utpro/file-manager';
export const PAGE_SIZE = 100;

const IMAGE_EXT = ['.jpg', '.jpeg', '.png', '.gif', '.svg', '.webp', '.bmp', '.ico'];
const VIDEO_EXT = ['.mp4', '.webm', '.ogg'];
const AUDIO_EXT = ['.mp3', '.wav', '.ogg'];
const MEDIA_EXT = [...IMAGE_EXT, ...VIDEO_EXT, ...AUDIO_EXT, '.pdf'];

export const isMedia = (ext) => MEDIA_EXT.includes(ext);
export const isImage = (ext) => IMAGE_EXT.includes(ext);
export const isVideo = (ext) => VIDEO_EXT.includes(ext);
export const isAudio = (ext) => AUDIO_EXT.includes(ext);
export const isPdf = (ext) => ext === '.pdf';

export function formatSize(bytes) {
    if (!bytes) return '';
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / 1048576).toFixed(1) + ' MB';
}

const ICON_MAP = {
    folder: 'icon-folder',
    image: 'icon-picture',
    script: 'icon-script',
    style: 'icon-brush',
    markup: 'icon-code',
    config: 'icon-document-html',
    zip: 'icon-zip',
    default: 'icon-document',
};

export function getIcon(item) {
    let name = ICON_MAP.default;
    if (item.type === 'folder') name = ICON_MAP.folder;
    else {
        const e = item.extension;
        if (IMAGE_EXT.includes(e)) name = ICON_MAP.image;
        else if (['.js', '.ts', '.mjs', '.jsx', '.tsx'].includes(e)) name = ICON_MAP.script;
        else if (['.css', '.scss', '.less'].includes(e)) name = ICON_MAP.style;
        else if (['.cshtml', '.razor', '.html', '.htm'].includes(e)) name = ICON_MAP.markup;
        else if (['.json', '.xml', '.yaml', '.yml', '.config'].includes(e)) name = ICON_MAP.config;
        else if (e === '.zip') name = ICON_MAP.zip;
    }
    return html`<uui-icon name=${name}></uui-icon>`;
}

const LANG_MAP = {
    '.js': 'javascript', '.mjs': 'javascript', '.jsx': 'javascript',
    '.ts': 'typescript', '.tsx': 'typescript',
    '.json': 'json', '.html': 'html', '.htm': 'html',
    '.cshtml': 'razor', '.razor': 'razor',
    '.css': 'css', '.scss': 'scss', '.less': 'less',
    '.xml': 'xml', '.config': 'xml', '.csproj': 'xml',
    '.props': 'xml', '.targets': 'xml', '.sln': 'xml', '.svg': 'xml',
    '.md': 'markdown', '.cs': 'csharp', '.sql': 'sql',
    '.yaml': 'yaml', '.yml': 'yaml',
    '.sh': 'shell', '.bat': 'bat', '.cmd': 'bat', '.ps1': 'powershell',
};

export const getEditorLanguage = (ext) => LANG_MAP[ext] || 'plaintext';
