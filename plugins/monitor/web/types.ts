// The monitor's view of a turn: its trace and the run's frames (docs/trace-events.md).

/**
 * One event as it crossed the AG-UI wire, kept so the monitor can show what the rest of the screen drops.
 * `seq` counts frames of the run from 1 and `atMs` is measured from its first frame. A frame of a turn recorded before
 * agui-protocol-only may be a custom trace frame, carrying `traceSeq` and no `payload`.
 * `unparsed` holds the raw text of a frame whose JSON could not be read.
 */
export interface AguiFrame {
  seq: number;
  atMs: number;
  /** The protocol event type; the SSE frame's own name when the payload could not be read. */
  type: string;
  /** A custom event's name. */
  name?: string;
  bytes: number;
  /** The sequence number of the trace event a `maf-lab/trace` frame carried. */
  traceSeq?: number;
  payload?: unknown;
  unparsed?: string;
  /** Set by the server when the run's size cap left this frame without its payload. */
  truncated?: boolean;
}

// ---- Turn trace (behind the scenes). See docs/trace-events.md. ----

export type TraceKind =
  | 'turn.start'
  | 'intent'
  | 'domain'
  | 'boundary'
  | 'history'
  | 'prompt'
  | 'model.request'
  | 'model.response'
  | 'tool.forced'
  | 'tool.call'
  | 'tool.result'
  | 'retrieval'
  | 'envelope'
  | 'tool.unknown'
  | 'audit'
  | 'adjustment'
  | 'guardrail'
  | 'sources'
  | 'card'
  | 'focus'
  | 'signals'
  | 'memory'
  | 'turn.end';

export interface TraceEvent {
  seq: number;
  atMs: number;
  /** One of TraceKind; unknown kinds are still shown in the timeline. */
  kind: string;
  title: string;
  durationMs?: number | null;
  data: unknown;
  truncated: boolean;
}

export interface TurnTraceDocument {
  turnId: string;
  conversationId: string;
  createdAt: string;
  events: TraceEvent[];
  /** The run's frames, or null for a turn answered before they were kept. */
  aguiFrames?: AguiFrame[] | null;
}
