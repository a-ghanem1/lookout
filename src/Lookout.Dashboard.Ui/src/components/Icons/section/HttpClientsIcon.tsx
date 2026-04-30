export function HttpClientsIcon({ className }: { className?: string }) {
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
      <circle cx="6.5" cy="8" r="4.5" />
      <path d="M11 3.5l2.5 2-2.5 2" />
      <line x1="13.5" y1="5.5" x2="8" y2="5.5" />
    </svg>
  );
}
