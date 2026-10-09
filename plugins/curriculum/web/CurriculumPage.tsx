import { Fragment } from 'react';
import { PageLink, contributions, usePlugins } from '@maf/plugin-api';
import page from '@maf/shared/Page.module.css';
import { CURRICULUM, NOT_COVERED, type CurriculumEntry } from './curriculum';
import styles from './Curriculum.module.css';

const GAPS_ID = 'not-covered';

// Static reference content: no api call and no session, so it reads before a persona is picked.
export function CurriculumPage() {
  const optionalPages = new Set(
    contributions(usePlugins(), 'routes').map(({ item }) => `/${item.path}`),
  );
  return (
    <section className={page.page}>
      <h1 className={page.heading}>Curriculum</h1>
      <p className={styles.intro}>
        Where the concepts and rules of the 5-day Fullstack AI Engineer study plan (revision 7) are
        applied in this lab. Each card says in a few sentences how the lab uses the concept, which
        files implement it, which spec states it and, where there is one, the screen that shows it.
      </p>

      <nav aria-label="Curriculum sections">
        <ul className={styles.toc}>
          {CURRICULUM.map((section) => (
            <li key={section.id}>
              <a href={`#${section.id}`}>{section.title}</a>
            </li>
          ))}
          <li>
            <a href={`#${GAPS_ID}`}>Not covered</a>
          </li>
        </ul>
      </nav>

      {CURRICULUM.map((section) => (
        <section
          key={section.id}
          id={section.id}
          className={styles.section}
          aria-labelledby={`${section.id}-title`}
        >
          <h2 id={`${section.id}-title`} className={styles.sectionTitle}>
            {section.title}
          </h2>
          <p className={styles.sectionNote}>{section.note}</p>
          <div className={styles.grid}>
            {section.entries.map((entry) => (
              <Entry key={entry.concept} entry={entry} optionalPages={optionalPages} />
            ))}
          </div>
        </section>
      ))}

      <section id={GAPS_ID} className={styles.section} aria-labelledby={`${GAPS_ID}-title`}>
        <h2 id={`${GAPS_ID}-title`} className={styles.sectionTitle}>
          Not covered
        </h2>
        <p className={styles.sectionNote}>Parts of the plan the lab does not implement, and why.</p>
        <ul className={styles.gaps}>
          {NOT_COVERED.map((gap) => (
            <li key={gap.topic}>
              <strong>{gap.topic}.</strong> {gap.reason}
            </li>
          ))}
        </ul>
      </section>
    </section>
  );
}

function Entry({
  entry,
  optionalPages,
}: {
  entry: CurriculumEntry;
  optionalPages: ReadonlySet<string>;
}) {
  return (
    <article className={styles.entry}>
      <h3 className={styles.concept}>{entry.concept}</h3>
      <p className={styles.summary}>{entry.summary}</p>
      <ul className={styles.paths} aria-label="Where in the code">
        {entry.paths.map((path) => (
          <li key={path} className={page.mono}>
            <PathText path={path} />
          </li>
        ))}
      </ul>
      <div className={styles.meta}>
        <span className={page.tag} title="OpenSpec capability">
          spec: {entry.spec}
        </span>
        {entry.screen &&
          (!entry.screen.optional || optionalPages.has(entry.screen.to.split('#')[0])) && (
            <PageLink to={entry.screen.to}>See it on {entry.screen.label} →</PageLink>
          )}
      </div>
    </article>
  );
}

// Lets a long path wrap after a slash rather than in the middle of a name.
function PathText({ path }: { path: string }) {
  return path.split('/').map((part, i) => (
    <Fragment key={i}>
      {i > 0 && (
        <>
          /<wbr />
        </>
      )}
      {part}
    </Fragment>
  ));
}
