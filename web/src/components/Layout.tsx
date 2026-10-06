import { useRef } from 'react';
import { NavLink, Outlet } from 'react-router';
import { DevTokenPicker } from './DevTokenPicker';
import { ThemeButton } from '../theme/ThemeButton';
import styles from './Layout.module.css';
import { useStickyHeader } from './useStickyHeader';
import { usePlugins } from '../plugins/context';
import { contributions } from '../plugins/registry';
import { useAuth } from '../auth/useAuth';

const LINKS = [
  { to: '/chat', label: 'Chat' },
  { to: '/evals', label: 'Evals' },
  { to: '/topology', label: 'Topology' },
  { to: '/telemetry', label: 'Telemetry' },
  { to: '/coverage', label: 'Coverage' },
  { to: '/admin/index', label: 'Index admin' },
  { to: '/admin/feedback', label: 'Feedback review' },
  { to: '/admin/compliance', label: 'Compliance' },
  { to: '/admin/jev', label: 'Jev' },
  { to: '/admin/a2a', label: 'Agent to agent' },
  { to: '/curriculum', label: 'Curriculum' },
];

// Developer tools that run beside the lab (compose profile `inspectors`), on the same host as this page.
const INSPECTORS = [
  { port: 7172, label: 'A2A Inspector' },
  { port: 7173, label: 'MCP Inspector' },
  { port: 7174, label: 'Redis Insight' },
];

function inspectorUrl(port: number): string {
  return `${window.location.protocol}//${window.location.hostname}:${port}`;
}

export function Layout() {
  const headerRef = useRef<HTMLElement>(null);
  const sticky = useStickyHeader(headerRef);
  const plugins = usePlugins();
  const { session } = useAuth();
  const admin = session?.user.role === 'TENANT_ADMIN';
  const pluginLinks = contributions(plugins, 'nav').filter(({ item }) => !item.adminOnly || admin);

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
          {pluginLinks.map(({ plugin, item }) =>
            item.to ? (
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
          {INSPECTORS.map((tool) => (
            <a
              key={tool.port}
              href={inspectorUrl(tool.port)}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`${tool.label} (opens in a new tab)`}
              className={styles.link}
            >
              {tool.label}
              <span className={styles.external} aria-hidden="true">
                ↗
              </span>
            </a>
          ))}
        </nav>
        <DevTokenPicker />
        <ThemeButton />
      </header>
      <main className={styles.main}>
        <Outlet />
      </main>
    </div>
  );
}
