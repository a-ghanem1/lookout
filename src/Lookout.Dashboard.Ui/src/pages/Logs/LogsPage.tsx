import { EntryListShell } from '../../components/EntryList/EntryListShell';

export function LogsPage() {
  return (
    <EntryListShell
      title="Logs"
      total={0}
      loading={false}
      items={[]}
      renderRow={() => null}
      emptyMessage="No logs captured for the current retention window."
    />
  );
}
