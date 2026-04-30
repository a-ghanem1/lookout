import { EntryListShell } from '../../components/EntryList/EntryListShell';

export function ExceptionsPage({ id: _id }: { id?: string } = {}) {
  return (
    <EntryListShell
      title="Exceptions"
      total={0}
      loading={false}
      items={[]}
      renderRow={() => null}
      emptyMessage='No exceptions captured. Throw something to test it: throw new InvalidOperationException("test") from any endpoint.'
    />
  );
}
