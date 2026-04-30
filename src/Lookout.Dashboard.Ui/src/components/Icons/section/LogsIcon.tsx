export function LogsIcon({ className }: { className?: string }) {
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
      <line x1="3" y1="4" x2="13" y2="4" />
      <line x1="3" y1="7" x2="11" y2="7" />
      <line x1="3" y1="10" x2="13" y2="10" />
      <line x1="3" y1="13" x2="9" y2="13" />
    </svg>
  );
}
