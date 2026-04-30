export function CacheIcon({ className }: { className?: string }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 16 16"
      width="16"
      height="16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
    >
      <rect x="2" y="2" width="12" height="4" rx="1" />
      <rect x="2" y="8" width="12" height="4" rx="1" />
      <circle cx="12.5" cy="4" r=".75" fill="currentColor" stroke="none" />
      <circle cx="12.5" cy="10" r=".75" fill="currentColor" stroke="none" />
    </svg>
  );
}
