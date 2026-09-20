import { NavLink, Outlet } from 'react-router';
import { DevTokenPicker } from './DevTokenPicker';
import styles from './Layout.module.css';

const LINKS = [
  { to: '/chat', label: 'Chat' },
  { to: '/evals', label: 'Evals' },
  { to: '/topology', label: 'Topology' },
  { to: '/admin/index', label: 'Index admin' },
  { to: '/admin/feedback', label: 'Feedback review' },
  { to: '/admin/compliance', label: 'Compliance' },
  { to: '/admin/a2a', label: 'Agent to agent' },
];

export function Layout() {
  return (
    <div className={styles.shell}>
      <header className={styles.header}>
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
        </nav>
        <DevTokenPicker />
      </header>
      <main className={styles.main}>
        <Outlet />
      </main>
    </div>
  );
}
