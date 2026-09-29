const SAFE_SCHEMES = /^(https?:|mailto:)/i;

/** A link or image address survives only with a scheme that cannot run script; anything else becomes no address. */
export function safeUrl(url: string): string | null {
  const trimmed = url.trim();
  return SAFE_SCHEMES.test(trimmed) ? trimmed : null;
}
