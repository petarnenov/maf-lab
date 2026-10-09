import { contributions, PluginBoundary, useAuth, usePageQuery, usePlugins } from '../plugins/api';
import { PluginSwitches } from './PluginSwitches';
import { AdminOverview } from './AdminOverview';
import { OperatorAccessAudit } from './OperatorAccessAudit';
import { OperatorContentAccess } from './OperatorContentAccess';
import { PlatformContentAccess } from './PlatformContentAccess';
import styles from './Page.module.css';
import type { ReactNode } from 'react';

function SectionBody({ render }: { render: () => ReactNode }) {
  return <>{render()}</>;
}

/** Dashboard layout for role-scoped plugin contributions; the section owners supply every panel. */
export function AdminDashboard({ platform }: { platform: boolean }) {
  const { session } = useAuth();
  const plugins = usePlugins();
  const [query, setQuery] = usePageQuery();
  const sections = contributions(
    plugins,
    platform ? 'platformAdminSections' : 'tenantAdminSections',
  );
  const selected = query.get('section');
  const shown = selected ? sections.filter(({ item }) => item.id === selected) : sections;
  return (
    <div className={styles.page}>
      <h1 className={styles.heading}>
        {platform ? 'Platform administration' : 'Tenant administration'}
      </h1>
      <p className={styles.notice}>Organization: {session?.user.tenantId}</p>
      <nav className={styles.actions} aria-label="Administration sections">
        <button onClick={() => setQuery({})}>Overview</button>
        {sections.map(({ plugin, item }) => (
          <button
            key={`${plugin}:${item.id}`}
            onClick={() => setQuery({ section: item.id })}
            aria-pressed={selected === item.id}
          >
            {item.label}
          </button>
        ))}
      </nav>
      {!selected && <PluginSwitches key={session?.token} platform={platform} />}
      {!selected && <AdminOverview key={session?.token} platform={platform} />}
      {!selected && !platform && <OperatorAccessAudit key={session?.token} />}
      {!selected && !platform && <OperatorContentAccess key={session?.token} />}
      {!selected && platform && <PlatformContentAccess key={session?.token} />}
      {selected && shown.length === 0 && (
        <p className={styles.notice}>This section is not available for your organization.</p>
      )}
      <div className={styles.cards}>
        {shown.map(({ plugin, item, health }) => (
          <section className={styles.card} key={`${plugin}:${item.id}`} aria-label={item.label}>
            <h2 className={styles.subheading}>{item.label}</h2>
            {health === 'unavailable' ? (
              <p className={styles.notice}>Unavailable right now.</p>
            ) : (
              <PluginBoundary plugin={plugin}>
                <SectionBody render={item.render} />
              </PluginBoundary>
            )}
          </section>
        ))}
      </div>
    </div>
  );
}
