import type { ReactNode } from 'react';
import ReactMarkdown, { type Components } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import styles from './Markdown.module.css';
import { safeUrl } from './safeUrl';

/**
 * The assistant's answer as GitHub-flavoured markdown (add-markdown-rendering), locked down so that nothing in an
 * answer can run script, load a resource or navigate the page: raw HTML is shown as text, links are kept only for
 * http, https and mailto and open in a new tab, and images show their alt text. Rendered as React elements, never as
 * HTML strings.
 */
export function Markdown({ text }: { text: string }) {
  return (
    <div className={styles.markdown}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm, htmlAsText]}
        urlTransform={safeUrl}
        components={components}
      >
        {text}
      </ReactMarkdown>
    </div>
  );
}

interface MdNode {
  type: string;
  value?: string;
  children?: MdNode[];
}

/** Turns every raw-HTML node into plain text, so `<script>` reads as the characters it is and never becomes an element. */
function htmlAsText() {
  const walk = (node: MdNode) => {
    if (node.type === 'html') node.type = 'text';
    node.children?.forEach(walk);
  };
  return (tree: MdNode) => walk(tree);
}

/** A table cell holding a number, an amount or a percentage is aligned right, as the data cards align theirs. */
const NUMERIC =
  /^[\s+\-−–]*[$€]?\s?\d[\d\s\u00a0\u202f.,]*(?:\s?(?:%|\$|€|USD|EUR|BGN|лв\.?|k|K|M))?\s*$/;

function textOf(children: ReactNode): string {
  if (typeof children === 'string' || typeof children === 'number') return String(children);
  if (Array.isArray(children)) return children.map(textOf).join('');
  return '';
}

const components: Components = {
  a: ({ href, children }) =>
    href ? (
      <a href={href} target="_blank" rel="noopener noreferrer">
        {children}
      </a>
    ) : (
      <span>{children}</span>
    ),
  img: ({ alt }) => <span className={styles.imageAlt}>{alt ?? ''}</span>,
  table: ({ children }) => (
    <div className={styles.tableScroll}>
      <table>{children}</table>
    </div>
  ),
  td: ({ children, style }) => (
    <td style={style} className={NUMERIC.test(textOf(children)) ? styles.num : undefined}>
      {children}
    </td>
  ),
};
