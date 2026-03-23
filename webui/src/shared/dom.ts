export function getFileNameFromPath(path: string | undefined): string {
  if (!path) {
    return '';
  }

  const segments = path.split(/[\\/]/);
  return segments[segments.length - 1] ?? '';
}

export function sleep(milliseconds: number): Promise<void> {
  return new Promise(resolve => {
    window.setTimeout(resolve, milliseconds);
  });
}

function extractGuid(source: string | undefined): string | null {
  if (!source) {
    return null;
  }

  const patterns = [
    /(?:^|[?&#])id=([0-9a-fA-F-]{32,36})/i,
    /\/details\/([0-9a-fA-F-]{32,36})/i,
    /\/items\/([0-9a-fA-F-]{32,36})/i
  ];

  for (const pattern of patterns) {
    const match = source.match(pattern);
    if (match?.[1]) {
      return match[1];
    }
  }

  return null;
}

function hasDetailsRoute(source: string | undefined): boolean {
  if (!source) {
    return false;
  }

  return /(?:^|#)\/details(?:[/?&]|$)/i.test(source) || /\/details\/[0-9a-fA-F-]{32,36}/i.test(source);
}

export function getCurrentItemId(): string | null {
  return extractGuid(window.location.hash)
    ?? extractGuid(window.location.search)
    ?? extractGuid(window.location.pathname)
    ?? extractGuid(window.location.href);
}

export function isCurrentDetailsRoute(): boolean {
  return hasDetailsRoute(window.location.hash)
    || hasDetailsRoute(window.location.pathname)
    || hasDetailsRoute(window.location.href);
}
