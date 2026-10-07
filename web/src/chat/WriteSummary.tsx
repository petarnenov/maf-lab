import type { SummarySchema } from '../api/types';
import { formatDate } from '../shared/format';
import styles from './ConfirmationCard.module.css';

interface Props {
  summary: unknown;
  schema?: SummarySchema | null;
}

/**
 * The summary of a write waiting for a person, as its flow's schema describes it: each property the summary has, by its
 * `title`, in the schema's property order (JSON Schema 2020-12 annotations). Display only. Without a schema there is
 * nothing it can name, so it shows nothing and the card shows the question alone.
 */
export function WriteSummary({ summary, schema }: Props) {
  const properties = Object.entries(schema?.properties ?? {});
  if (!isRecord(summary) || properties.length === 0) return null;
  const facts = properties.filter(([key]) => summary[key] !== undefined && summary[key] !== null);
  if (facts.length === 0) return null;

  return (
    <dl className={styles.facts} data-testid="write-summary">
      {facts.map(([key, property]) => (
        <div className={styles.fact} key={key}>
          <dt>{property.title ?? key}</dt>
          <dd>{display(summary[key], property.type, property.format)}</dd>
        </div>
      ))}
    </dl>
  );
}

function display(value: unknown, type?: string, format?: string): string {
  if (
    typeof value === 'number' &&
    (type === 'number' || type === 'integer' || type === undefined)
  ) {
    return new Intl.NumberFormat().format(value);
  }
  if (typeof value === 'string' && format === 'date') {
    const date = new Date(`${value}T00:00:00`);
    return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString();
  }
  if (typeof value === 'string' && format === 'date-time') return formatDate(value);
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
