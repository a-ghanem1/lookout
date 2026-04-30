import { EntryListShell } from '../../components/EntryList/EntryListShell';

export function JobsPage() {
  return (
    <EntryListShell
      title="Jobs"
      total={0}
      loading={false}
      items={[]}
      renderRow={() => null}
      emptyMessage="No Hangfire jobs captured. Enqueue a job to start capturing."
    />
  );
}
