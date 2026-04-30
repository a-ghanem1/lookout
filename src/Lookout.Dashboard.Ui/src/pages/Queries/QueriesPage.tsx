import { EntryListShell } from '../../components/EntryList/EntryListShell';

export function QueriesPage({ id: _id }: { id?: string } = {}) {
  return (
    <EntryListShell
      title="Queries"
      total={0}
      loading={false}
      items={[]}
      renderRow={() => null}
      emptyMessage="No database queries captured yet. Run an EF Core or ADO.NET query to start capturing."
    />
  );
}
