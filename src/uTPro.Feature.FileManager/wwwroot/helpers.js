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
    folder: '\u{1F4C1}',
    image: '\u{1F5BC}\uFE0F',
    script: '\u{1F4DC}',
    style: '\u{1F3A8}',
    markup: '\u{1F4C4}',
    config: '\u2699\uFE0F',
    csharp: '\u{1F537}',
    default: '\u{1F4CE}',
};

export function getIcon(item) {
    if (item.type === 'folder') return ICON_MAP.folder;
    const e = item.extension;
    if (IMAGE_EXT.includes(e)) return ICON_MAP.image;
    if (['.js', '.ts', '.mjs', '.jsx', '.tsx'].includes(e)) return ICON_MAP.script;
    if (['.css', '.scss', '.less'].includes(e)) return ICON_MAP.style;
    if (['.cshtml', '.razor', '.html', '.htm'].includes(e)) return ICON_MAP.markup;
    if (['.json', '.xml', '.yaml', '.yml', '.config'].includes(e)) return ICON_MAP.config;
    if (e === '.cs') return ICON_MAP.csharp;
    return ICON_MAP.default;
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
