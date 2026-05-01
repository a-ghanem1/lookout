import { Clock } from 'lucide-react';

export function JobsIcon({ className }: { className?: string }) {
  return <Clock size={16} strokeWidth={1.75} className={className} aria-hidden />;
}
