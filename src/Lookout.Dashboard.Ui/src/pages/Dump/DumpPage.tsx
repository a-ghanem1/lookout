import { EntryListShell } from '../../components/EntryList/EntryListShell';

export function DumpPage() {
  return (
    <EntryListShell
      title="Dump"
      total={0}
      loading={false}
      items={[]}
      renderRow={() => null}
      emptyMessage='No Lookout.Dump() calls captured yet. Add Lookout.Dump(obj, "label") anywhere in your request path to start capturing.'
    />
  );
}
