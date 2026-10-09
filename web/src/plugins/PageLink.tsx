import type { ReactNode } from 'react';
import { Link } from 'react-router';

/**
 * A link to another page of the app, for a plugin (extract-curriculum-plugin): the core's router follows it in the
 * page, without a reload, and the core sees the route change. A plugin never imports react-router itself
 * (introduce-plugins part C); this is the one way it links a page.
 */
export function PageLink({
  to,
  children,
  className,
}: {
  to: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <Link to={to} className={className}>
      {children}
    </Link>
  );
}
