import { EntryListShell } from '../../components/EntryList/EntryListShell';

export function CachePage() {
  return (
    <EntryListShell
      title="Cache"
      total={0}
      loading={false}
      items={[]}
      renderRow={() => null}
      emptyMessage="No cache events captured. Use IMemoryCache or IDistributedCache to start capturing."
    />
  );
}
