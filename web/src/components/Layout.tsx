import { useRef } from 'react';
import { NavLink, Outlet } from 'react-router';
import { ThemeButton } from '../theme/ThemeButton';
import styles from './Layout.module.css';
import hidden from '../shared/VisuallyHidden.module.css';
import { useStickyHeader } from './useStickyHeader';
import { PluginBoundary } from '../plugins/PluginBoundary';
import { usePlugins } from '../plugins/context';
import { contributions } from '../plugins/registry';
import { useAuth } from '../auth/useAuth';
import { ContentAccessBanner } from '../shared/ContentAccessBanner';

const LINKS = [{ to: '/chat', label: 'Chat' }];

export function Layout() {
  const headerRef = useRef<HTMLElement>(null);
  const sticky = useStickyHeader(headerRef);
  const plugins = usePlugins();
  const { session } = useAuth();
  const admin = session?.user.role === 'TENANT_ADMIN';
  const pluginLinks = contributions(plugins, 'nav').filter(
    ({ item }) =>
      (!item.adminOnly || admin) &&
      (!item.platformAdminOnly || session?.user.role === 'PLATFORM_ADMIN'),
  );

  return (
    <div className={styles.shell}>
      <header ref={headerRef} className={styles.header} data-sticky={sticky}>
        <span className={styles.brand}>maf-lab</span>
        <nav className={styles.nav} aria-label="Main">
          {LINKS.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              className={({ isActive }) =>
                isActive ? `${styles.link} ${styles.active}` : styles.link
              }
            >
              {link.label}
            </NavLink>
          ))}
          {pluginLinks.map(({ plugin, item, health }) =>
            // A plugin whose own service is down is shown, not hidden, and cannot be followed: a placeholder link
            // (no href), disabled the WAI-ARIA way and muted in the page's theme.
            health === 'unavailable' ? (
              <a
                key={`${plugin}:${item.to ?? item.href}`}
                role="link"
                aria-disabled="true"
                className={`${styles.link} ${styles.unavailable}`}
              >
                {item.label}
                {item.href && (
                  <span className={styles.external} aria-hidden="true">
                    ↗
                  </span>
                )}{' '}
                <span className={hidden.visuallyHidden}>(unavailable)</span>
              </a>
            ) : item.to ? (
              <NavLink
                key={`${plugin}:${item.to}`}
                to={item.to}
                className={({ isActive }) =>
                  isActive ? `${styles.link} ${styles.active}` : styles.link
                }
              >
                {item.label}
              </NavLink>
            ) : (
              <a
                key={`${plugin}:${item.href}`}
                href={item.href}
                target="_blank"
                rel="noopener noreferrer"
                aria-label={`${item.label} (opens in a new tab)`}
                className={styles.link}
              >
                {item.label}
                <span className={styles.external} aria-hidden="true">
                  ↗
                </span>
              </a>
            ),
          )}
        </nav>
        {contributions(plugins, 'authControls').map(({ plugin, item }) => (
          <PluginBoundary key={`${plugin}:${item.id}`} plugin={plugin}>
            {item.render()}
          </PluginBoundary>
        ))}
        <ThemeButton />
      </header>
      <main className={styles.main}>
        <ContentAccessBanner />
        <Outlet />
      </main>
    </div>
  );
}
