# Feedback review

Admins open `/admin/feedback` to review flagged turns in their own tenant. The queue shows signals, tool calls and
available source chunks. Labels preserve the existing selection, retrieval and generation JSONL formats. Core
turns and labels remain in SQLite; an installed dataset exporter also appends the row to its dataset. Without an
exporter, labels still save and mark the turn as labelled. Repeated export of a row uses its stable id.

Installed plugins may contribute review panels; the monitor's stored trace appears only while it is in use.
Removing this plugin removes the queue, labelling routes and navigation entry. User feedback remains available
through the chat and `POST /api/feedback`.
