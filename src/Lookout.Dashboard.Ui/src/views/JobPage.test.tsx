import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { EntryDto, JobEnqueueEntryContent, JobExecutionEntryContent } from '../api/types';
import { JobBody } from './JobPage';

function jobEnqueueEntry(overrides: Partial<JobEnqueueEntryContent> = {}): EntryDto {
  const content: JobEnqueueEntryContent = {
    jobId: 'hangfire-job-1',
    jobType: 'WebApp.Jobs.SendEmailJob',
    methodName: 'Execute',
    arguments: [
      { name: 'email', type: 'System.String', value: '"alice@example.com"' },
      { name: 'subject', type: 'System.String', value: '"Hello"' },
    ],
    queue: 'default',
    state: 'Enqueued',
    errorType: null,
    errorMessage: null,
    ...overrides,
  };
  return {
    id: 'entry-1',
    type: 'job-enqueue',
    timestamp: 1_700_000_000_000,
    requestId: 'req-42',
    durationMs: 0.5,
    tags: { 'job.id': content.jobId, 'job.enqueue': 'true' },
    content,
  };
}

function jobExecutionEntry(
  state: 'Succeeded' | 'Failed' = 'Succeeded',
  overrides: Partial<JobExecutionEntryContent> = {},
): EntryDto {
  const content: JobExecutionEntryContent = {
    jobId: 'hangfire-job-1',
    jobType: 'WebApp.Jobs.SendEmailJob',
    methodName: 'Execute',
    enqueueRequestId: 'req-42',
    state,
    errorType: state === 'Failed' ? 'System.InvalidOperationException' : null,
    errorMessage: state === 'Failed' ? 'SMTP failed' : null,
    ...overrides,
  };
  return {
    id: 'entry-2',
    type: 'job-execution',
    timestamp: 1_700_000_001_000,
    requestId: 'req-42',
    durationMs: 130.2,
    tags: { 'job.id': content.jobId, 'job.execute': 'true', 'job.state': state },
    content,
  };
}

describe('JobPage — enqueue entry', () => {
  it('renders job-enqueue-detail testid', () => {
    render(<JobBody entry={jobEnqueueEntry()} />);
    expect(screen.getByTestId('job-enqueue-detail')).toBeInTheDocument();
  });

  it('renders ENQ type badge', () => {
    render(<JobBody entry={jobEnqueueEntry()} />);
    expect(screen.getByText('ENQ')).toBeInTheDocument();
  });

  it('renders method name in title', () => {
    render(<JobBody entry={jobEnqueueEntry()} />);
    expect(screen.getByTestId('job-title')).toHaveTextContent('SendEmailJob.Execute');
  });

  it('renders meta fields: job id, queue, state', () => {
    render(<JobBody entry={jobEnqueueEntry()} />);
    const meta = screen.getByTestId('job-meta');
    expect(meta).toHaveTextContent('hangfire-job-1');
    expect(meta).toHaveTextContent('default');
    expect(meta).toHaveTextContent('Enqueued');
  });

  it('renders arguments table', () => {
    render(<JobBody entry={jobEnqueueEntry()} />);
    const args = screen.getByTestId('job-arguments');
    expect(args).toHaveTextContent('email');
    expect(args).toHaveTextContent('"alice@example.com"');
    expect(args).toHaveTextContent('subject');
    expect(args).toHaveTextContent('"Hello"');
  });

  it('renders error section when errorType present', () => {
    render(
      <JobBody
        entry={jobEnqueueEntry({
          errorType: 'System.Exception',
          errorMessage: 'create failed',
        })}
      />,
    );
    const err = screen.getByTestId('job-error');
    expect(err).toHaveTextContent('System.Exception');
    expect(err).toHaveTextContent('create failed');
  });

  it('does not render arguments section when arguments is empty', () => {
    render(<JobBody entry={jobEnqueueEntry({ arguments: [] })} />);
    expect(screen.queryByTestId('job-arguments')).not.toBeInTheDocument();
  });

  it('returns not-found for unknown entry types', () => {
    const unknownEntry: EntryDto = {
      id: 'x',
      type: 'ef',
      timestamp: 0,
      durationMs: 0,
      tags: {},
      content: {},
    };
    render(<JobBody entry={unknownEntry} />);
    expect(screen.getByTestId('job-not-found')).toBeInTheDocument();
  });
});

describe('JobPage — execution entry', () => {
  it('renders job-execution-detail testid', () => {
    render(<JobBody entry={jobExecutionEntry()} />);
    expect(screen.getByTestId('job-execution-detail')).toBeInTheDocument();
  });

  it('renders EXEC type badge', () => {
    render(<JobBody entry={jobExecutionEntry()} />);
    expect(screen.getByText('EXEC')).toBeInTheDocument();
  });

  it('renders method name in title', () => {
    render(<JobBody entry={jobExecutionEntry()} />);
    expect(screen.getByTestId('job-title')).toHaveTextContent('SendEmailJob.Execute');
  });

  it('renders Succeeded state badge', () => {
    render(<JobBody entry={jobExecutionEntry('Succeeded')} />);
    expect(screen.getByTestId('job-state-badge')).toHaveTextContent('Succeeded');
  });

  it('renders Failed state badge', () => {
    render(<JobBody entry={jobExecutionEntry('Failed')} />);
    expect(screen.getByTestId('job-state-badge')).toHaveTextContent('Failed');
  });

  it('renders enqueue request link', () => {
    render(<JobBody entry={jobExecutionEntry()} />);
    const link = screen.getByTestId('enqueue-request-link');
    expect(link).toBeInTheDocument();
    expect(link).toHaveTextContent('req-42');
    expect(link).toHaveAttribute('href', '#/requests/req-42');
  });

  it('renders error section on failed execution', () => {
    render(<JobBody entry={jobExecutionEntry('Failed')} />);
    const err = screen.getByTestId('job-error');
    expect(err).toHaveTextContent('System.InvalidOperationException');
    expect(err).toHaveTextContent('SMTP failed');
  });

  it('does not render error section on succeeded execution', () => {
    render(<JobBody entry={jobExecutionEntry('Succeeded')} />);
    expect(screen.queryByTestId('job-error')).not.toBeInTheDocument();
  });

  it('does not render enqueue request link when enqueueRequestId is null', () => {
    render(<JobBody entry={jobExecutionEntry('Succeeded', { enqueueRequestId: null })} />);
    expect(screen.queryByTestId('enqueue-request-link')).not.toBeInTheDocument();
  });
});
