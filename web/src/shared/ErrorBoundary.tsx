import { Component, type ErrorInfo, type ReactNode } from 'react';
import styles from './Page.module.css';

interface Props {
  /** Named on the page so the user knows which part of the screen is missing. */
  area: string;
  children: ReactNode;
}

interface State {
  failed: boolean;
}

/**
 * Keeps a render failure inside one area of the screen instead of letting it unmount the root.
 *
 * React 19 still has no hook form of this; `getDerivedStateFromError` on a class is the only
 * supported mechanism. Nothing from the error reaches the page — not its type, message or stack —
 * for the same reason no other failure in this app puts internals in front of a user. The console
 * keeps the detail for whoever is debugging.
 *
 * There is no reset control: the caller gives the boundary a `key` that changes when the user
 * navigates, and React remounts the subtree, which clears the failure as a side effect of the move
 * they were making anyway.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`[${this.props.area}] render failed`, error, info.componentStack);
  }

  render() {
    if (this.state.failed) {
      return (
        <p className={styles.notice} role="alert">
          {this.props.area} could not be shown. The rest of the page is unaffected.
        </p>
      );
    }
    return this.props.children;
  }
}
