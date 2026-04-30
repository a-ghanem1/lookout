export function RequestsIcon({ className }: { className?: string }) {
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
      <path d="M1 4.5h11" />
      <path d="M9.5 2.5l3 2-3 2" />
      <path d="M15 11.5H4" />
      <path d="M6.5 9.5l-3 2 3 2" />
    </svg>
  );
}
