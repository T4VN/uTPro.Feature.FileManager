export const API_BASE = '/umbraco/management/api/v1/utpro/file-manager';
export const PAGE_SIZE = 100;

const MEDIA_EXTENSIONS = [
    '.jpg', '.jpeg', '.png', '.gif', '.svg', '.webp', '.bmp', '.ico',
    '.mp4', '.webm', '.ogg', '.mp3', '.wav', '.pdf'
];

const IMAGE_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.gif', '.svg', '.webp', '.bmp', '.ico'];
const VIDEO_EXTENSIONS = ['.mp4', '.webm', '.ogg'];
const AUDIO_EXTENSIONS = ['.mp3', '.wav', '.ogg'];

export function isMedia(ext) {
    return MEDIA_EXTENSIONS.includes(ext);
}

export function isImage(ext) {
    return IMAGE_EXTENSIONS.includes(ext);
}

export function isVideo(ext) {
    return VIDEO_EXTENSIONS.includes(ext);
}

export function isAudio(ext) {
    return AUDIO_EXTENSIONS.includes(ext);
}

export function isPdf(ext) {
    return ext === '.pdf';
}

export function formatSize(bytes) {
    if (!bytes) return '';
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / 1048576).toFixed(1) + ' MB';
}

export function getIcon(item) {
    if (item.type === 'folder') return '\u{1F4C1}';
    const ext = item.extension;
    if (['.jpg', '.jpeg', '.png', '.gif', '.svg', '.webp'].includes(ext)) return '\u{1F5BC}\uFE0F';
    if (ext === '.js' || ext === '.ts') return '\u{1F4DC}';
    if (ext === '.css' || ext === '.scss' || ext === '.less') return '\u{1F3A8}';
    if (ext === '.cshtml' || ext === '.razor' || ext === '.html') return '\u{1F4C4}';
    if (ext === '.json' || ext === '.xml' || ext === '.yaml' || ext === '.yml') return '\u2699\uFE0F';
    if (ext === '.cs') return '\u{1F537}';
    return '\u{1F4CE}';
}

export function getEditorLanguage(ext) {
    const map = {
        '.js': 'javascript', '.mjs': 'javascript', '.jsx': 'javascript',
        '.ts': 'typescript', '.tsx': 'typescript',
        '.json': 'json',
        '.html': 'html', '.htm': 'html', '.cshtml': 'razor', '.razor': 'razor',
        '.css': 'css', '.scss': 'scss', '.less': 'less',
        '.xml': 'xml', '.config': 'xml', '.csproj': 'xml', '.props': 'xml', '.targets': 'xml', '.sln': 'xml',
        '.md': 'markdown',
        '.cs': 'csharp',
        '.sql': 'sql',
        '.yaml': 'yaml', '.yml': 'yaml',
        '.sh': 'shell', '.bat': 'bat', '.cmd': 'bat', '.ps1': 'powershell',
        '.svg': 'xml',
    };
    return map[ext] || 'plaintext';
}
