/** The A2A activity screen's view of the firm's agents (GET /api/admin/a2a). No message content, only what happened. */

export interface InboundTask {
  taskId: string;
  partnerId: string;
  operation: string;
  state: string;
  createdAt: string;
  updatedAt: string;
  durationMs: number;
  cancellable: boolean;
}

export interface OutboundConsultation {
  taskId: string;
  agent: string;
  outcome: string;
  durationMs: number;
  at: string;
}

export interface PushDelivery {
  taskId: string;
  state: string;
  url: string;
  attempts: number;
  delivered: boolean;
  error?: string | null;
  at: string;
}

export interface A2AActivity {
  inbound: InboundTask[];
  outbound: OutboundConsultation[];
  deliveries: PushDelivery[];
}
