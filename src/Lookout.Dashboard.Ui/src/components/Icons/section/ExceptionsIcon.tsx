export function ExceptionsIcon({ className }: { className?: string }) {
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
      <path d="M7.13 2.5L1.63 12a1 1 0 00.87 1.5h11a1 1 0 00.87-1.5L8.87 2.5a1 1 0 00-1.74 0z" />
      <line x1="8" y1="6.5" x2="8" y2="9.5" />
      <circle cx="8" cy="11.25" r=".5" fill="currentColor" stroke="none" />
    </svg>
  );
}
