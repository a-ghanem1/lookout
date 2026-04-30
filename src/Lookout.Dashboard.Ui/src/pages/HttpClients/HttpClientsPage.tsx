import { EntryListShell } from '../../components/EntryList/EntryListShell';

export function HttpClientsPage({ id: _id }: { id?: string } = {}) {
  return (
    <EntryListShell
      title="HTTP clients"
      total={0}
      loading={false}
      items={[]}
      renderRow={() => null}
      emptyMessage="No outbound HTTP calls captured. Use a typed or named HttpClient to start capturing."
    />
  );
}
