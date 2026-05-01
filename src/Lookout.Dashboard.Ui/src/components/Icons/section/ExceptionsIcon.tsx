import { TriangleAlert } from 'lucide-react';

export function ExceptionsIcon({ className }: { className?: string }) {
  return <TriangleAlert size={16} strokeWidth={1.75} className={className} aria-hidden />;
}
