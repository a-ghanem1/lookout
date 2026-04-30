import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../../api/types';
import { JobsPage } from './JobsPage';

function makeEnqueueEntry(
  id: string,
  opts: {
    jobType?: string;
    methodName?: string;
    queue?: string;
    state?: string;
    requestId?: string;
  } = {},
) {
  const jobType = opts.jobType ?? 'WebApp.Jobs.SendEmailJob';
  const methodName = opts.methodName ?? 'Execute';
  const queue = opts.queue ?? 'default';
  const state = opts.state ?? 'Enqueued';
  return {
    id,
    type: 'job-enqueue',
    timestamp: Date.now() - 2000,
    requestId: opts.requestId,
    durationMs: 0.4,
    tags: {
      'job.id': `hf-${id}`,
      'job.type': jobType,
      'job.method': methodName,
      'job.queue': queue,
      'job.state': state,
      'job.enqueue': 'true',
    },
    content: {
      jobId: `hf-${id}`,
      jobType,
      methodName,
      arguments: [],
      queue,
      state,
      errorType: null,
      errorMessage: null,
    },
  };
}

function makeExecutionEntry(
  id: string,
  opts: {
    jobType?: string;
    methodName?: string;
    state?: string;
    requestId?: string;
    enqueueRequestId?: string;
    errorType?: string;
    errorMessage?: string;
  } = {},
) {
  const jobType = opts.jobType ?? 'WebApp.Jobs.SendEmailJob';
  const methodName = opts.methodName ?? 'Execute';
  const state = opts.state ?? 'Succeeded';
  return {
    id,
    type: 'job-execution',
    timestamp: Date.now() - 1000,
    requestId: opts.requestId,
    durationMs: 145,
    tags: {
      'job.id': `hf-${id}`,
      'job.type': jobType,
      'job.method': methodName,
      'job.state': state,
      'job.execute': 'true',
    },
    content: {
      jobId: `hf-${id}`,
      jobType,
      methodName,
      enqueueRequestId: opts.enqueueRequestId ?? null,
      state,
      errorType: opts.errorType ?? null,
      errorMessage: opts.errorMessage ?? null,
    },
  };
}

function makeResponse(entries: unknown[] = []): EntryListResponse {
  return { entries: entries as EntryListResponse['entries'], nextBefore: null };
}

describe('JobsPage', () => {
  const capturedUrls: string[] = [];

  beforeEach(() => {
    capturedUrls.length = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        capturedUrls.push(url);
        if (url.includes('type=job-execution')) {
          return new Response(
            JSON.stringify(
              makeResponse([
                makeExecutionEntry('exec-1', { state: 'Succeeded', requestId: 'req-x' }),
                makeExecutionEntry('exec-2', {
                  jobType: 'WebApp.Jobs.ProcessOrderJob',
                  methodName: 'RunAsync',
                  state: 'Failed',
                  errorType: 'System.InvalidOperationException',
                }),
              ]),
            ),
            { status: 200, headers: { 'content-type': 'application/json' } },
          );
        }
        // job-enqueue
        return new Response(
          JSON.stringify(
            makeResponse([
              makeEnqueueEntry('enq-1', { queue: 'default', requestId: 'req-x' }),
              makeEnqueueEntry('enq-2', { jobType: 'WebApp.Jobs.ProcessOrderJob', queue: 'critical' }),
            ]),
          ),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }) as unknown as typeof fetch,
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders execution entries after fetch', async () => {
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getByText('SendEmailJob.Execute')).toBeInTheDocument();
    });
    expect(screen.getAllByText('EXEC').length).toBeGreaterThan(0);
  });

  it('renders EXEC badge for execution entries', async () => {
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('EXEC').length).toBeGreaterThanOrEqual(1);
    });
  });

  it('renders Succeeded state badge', async () => {
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getByText('Succeeded')).toBeInTheDocument();
    });
  });

  it('renders Failed state badge', async () => {
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getByText('Failed')).toBeInTheDocument();
    });
  });

  it('default filter is Executions — hides enqueue rows', async () => {
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('EXEC').length).toBeGreaterThan(0);
    });

    expect(screen.queryByText('ENQ')).not.toBeInTheDocument();
  });

  it('switching to All shows enqueue rows', async () => {
    const user = userEvent.setup();
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('EXEC').length).toBeGreaterThan(0);
    });

    const kindGroup = screen.getByRole('group', { name: /kind filter/i });
    await user.click(within(kindGroup).getByRole('button', { name: /^All$/i }));

    await waitFor(() => {
      expect(screen.getAllByText('ENQ').length).toBeGreaterThan(0);
      expect(screen.getAllByText('EXEC').length).toBeGreaterThan(0);
    });
  });

  it('switching to Enqueues shows only enqueue rows', async () => {
    const user = userEvent.setup();
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('EXEC').length).toBeGreaterThan(0);
    });

    await user.click(screen.getByRole('button', { name: /^Enqueues$/i }));

    await waitFor(() => {
      expect(screen.getAllByText('ENQ').length).toBeGreaterThan(0);
      expect(screen.queryByText('EXEC')).not.toBeInTheDocument();
    });
  });

  it('status filter Failed hides Succeeded rows', async () => {
    const user = userEvent.setup();
    render(<JobsPage />);

    // Two execution rows visible by default: one Succeeded, one Failed
    await waitFor(() => {
      expect(screen.getAllByText('Succeeded').length).toBeGreaterThan(0);
    });

    await user.click(screen.getByRole('button', { name: /^Failed$/i }));

    await waitFor(() => {
      // Only the filter chip button should have "Succeeded" text — not a state badge in a row
      // The SendEmailJob (Succeeded) row should be gone; only ProcessOrderJob (Failed) row remains
      expect(screen.queryByText('SendEmailJob.Execute')).not.toBeInTheDocument();
      expect(screen.getByText('ProcessOrderJob.RunAsync')).toBeInTheDocument();
    });
  });

  it('job type search narrows rows', async () => {
    const user = userEvent.setup();
    // Switch to All first so both types are visible
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('EXEC').length).toBeGreaterThan(0);
    });

    await user.type(screen.getByRole('searchbox', { name: /search job type/i }), 'ProcessOrder');

    await waitFor(() => {
      expect(screen.getByText('ProcessOrderJob.RunAsync')).toBeInTheDocument();
      expect(screen.queryByText('SendEmailJob.Execute')).not.toBeInTheDocument();
    });
  });

  it('shows empty state when no entries', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse([])), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );

    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-empty-state')).toBeInTheDocument();
    });
    expect(screen.getByText(/No Hangfire jobs captured/)).toBeInTheDocument();
  });

  it('click execution row navigates to #/jobs/:id', async () => {
    const user = userEvent.setup();
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getByText('SendEmailJob.Execute')).toBeInTheDocument();
    });

    const rows = screen.getAllByRole('button');
    const execRow = rows.find((r) => r.textContent?.includes('SendEmailJob.Execute') && r.textContent?.includes('Succeeded'));
    if (execRow) await user.click(execRow);

    expect(window.location.hash).toContain('/jobs/exec-1');
  });

  it('renders request link for entries with requestId', async () => {
    render(<JobsPage />);

    await waitFor(() => {
      const link = screen.getAllByRole('link', { name: /view parent request/i });
      expect(link.length).toBeGreaterThan(0);
      expect(link[0]).toHaveAttribute('href', '#/requests/req-x');
    });
  });

  it('renders Background for entries without requestId', async () => {
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('Background').length).toBeGreaterThan(0);
    });
  });

  it('shows footer with count', async () => {
    render(<JobsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-list-footer')).toBeInTheDocument();
    });
  });

  it('sends type=job-enqueue and type=job-execution fetches', async () => {
    render(<JobsPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThanOrEqual(2));

    expect(capturedUrls.some((u) => u.includes('type=job-enqueue'))).toBe(true);
    expect(capturedUrls.some((u) => u.includes('type=job-execution'))).toBe(true);
  });
});
