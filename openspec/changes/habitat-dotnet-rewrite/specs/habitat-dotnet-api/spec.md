## ADDED Requirements

### Requirement: Config endpoint returns runtime configuration
The API SHALL expose `GET /api/config` returning a JSON object with `basePath`, `apiBasePath`, and `staticBasePath` fields reflecting the effective mount path of the current request.

#### Scenario: Config response shape
- **WHEN** a client sends `GET /api/config`
- **THEN** the response is HTTP 200 with `Content-Type: application/json` and a JSON body matching `{ "basePath": string, "apiBasePath": string, "staticBasePath": string }`

### Requirement: Metrics endpoint returns per-queue statistics
The API SHALL expose `GET /api/metrics` returning task count metrics for every queue registered in `absurd.queues`.

#### Scenario: Metrics response shape
- **WHEN** a client sends `GET /api/metrics`
- **THEN** the response is HTTP 200 with a JSON body `{ "queues": [ { "queueName", "queueLength", "queueVisibleLength", "totalMessages", "newestMsgAt", "oldestMsgAt", "scrapeTime" }, ... ] }`

#### Scenario: Empty system
- **WHEN** no queues exist in `absurd.queues`
- **THEN** the response is HTTP 200 with `{ "queues": [] }`

### Requirement: Tasks list endpoint supports filtering and pagination
The API SHALL expose `GET /api/tasks` accepting query parameters `q` (full-text search), `status`, `queue`, `taskName`, `taskId`, `after`, `before`, `page`, `perPage`; and returning a paginated task list.

#### Scenario: Unfiltered request
- **WHEN** a client sends `GET /api/tasks`
- **THEN** the response is HTTP 200 with a JSON body containing `items`, `total`, `hasMore`, `page`, `perPage`, `availableStatuses`, `availableQueues`, `availableTaskNames`

#### Scenario: Status filter returns only matching tasks
- **WHEN** a client sends `GET /api/tasks?status=failed`
- **THEN** all returned items have `state` equal to `"failed"`

#### Scenario: Invalid status filter returns empty list
- **WHEN** a client sends `GET /api/tasks?status=nonexistent`
- **THEN** the response is HTTP 200 with `items: []`

#### Scenario: Pagination respects page and perPage parameters
- **WHEN** a client sends `GET /api/tasks?page=2&perPage=10`
- **THEN** the response skips the first 10 results and returns at most 10 items

#### Scenario: perPage capped at 200
- **WHEN** a client sends `GET /api/tasks?perPage=999`
- **THEN** the response returns at most 200 items per page

### Requirement: Task detail endpoint returns full task data
The API SHALL expose `GET /api/tasks/{taskId}` returning the full task record including state, run history, checkpoints, parameters, and output.

#### Scenario: Existing task
- **WHEN** a client sends `GET /api/tasks/<valid-uuid>`
- **THEN** the response is HTTP 200 with the task's full JSON representation

#### Scenario: Non-existent task
- **WHEN** a client sends `GET /api/tasks/<uuid-not-in-db>`
- **THEN** the response is HTTP 404

#### Scenario: Invalid task ID format
- **WHEN** a client sends `GET /api/tasks/not-a-uuid`
- **THEN** the response is HTTP 404

### Requirement: Task retry endpoint re-queues a failed task
The API SHALL expose `POST /api/tasks/retry` accepting `{ "taskId": "<uuid>", "queueName": "<name>" }` and re-queuing the identified task.

#### Scenario: Successful retry
- **WHEN** a client sends `POST /api/tasks/retry` with a valid task ID for a failed task
- **THEN** the response is HTTP 200 and the task state transitions to `pending`

#### Scenario: Retry non-failed task
- **WHEN** a client sends `POST /api/tasks/retry` for a task that is not in a retryable state
- **THEN** the response is HTTP 400 with an error message

### Requirement: Queues list endpoint returns all queues
The API SHALL expose `GET /api/queues` returning all queues with metadata.

#### Scenario: Queues response shape
- **WHEN** a client sends `GET /api/queues`
- **THEN** the response is HTTP 200 with a JSON array of queue objects each containing `queueName`, `createdAt`

### Requirement: Queue resource endpoint returns queue details and task summary
The API SHALL expose `GET /api/queues/{queueName}` returning detailed metrics and recent activity for the named queue.

#### Scenario: Existing queue
- **WHEN** a client sends `GET /api/queues/<valid-queue-name>`
- **THEN** the response is HTTP 200 with queue detail JSON

#### Scenario: Non-existent queue
- **WHEN** a client sends `GET /api/queues/<unknown-queue>`
- **THEN** the response is HTTP 404

### Requirement: Events endpoint returns the event log
The API SHALL expose `GET /api/events` returning recent events from all queues, supporting `queue`, `taskId`, `after`, `before`, `page`, and `perPage` query parameters.

#### Scenario: Events response shape
- **WHEN** a client sends `GET /api/events`
- **THEN** the response is HTTP 200 with a JSON body containing `items`, `hasMore`, `page`, `perPage`

#### Scenario: Filter events by task ID
- **WHEN** a client sends `GET /api/events?taskId=<uuid>`
- **THEN** all returned events are associated with the specified task

### Requirement: All API endpoints return JSON with consistent error shape
All API endpoints SHALL set `Content-Type: application/json` on success and return a JSON object with an `error` string field on failure (4xx/5xx).

#### Scenario: Server error during query
- **WHEN** the database returns an unexpected error during an API call
- **THEN** the response is HTTP 500 with `Content-Type: application/json` and `{ "error": "<message>" }`
