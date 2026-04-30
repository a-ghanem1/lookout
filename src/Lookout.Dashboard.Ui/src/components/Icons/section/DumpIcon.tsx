export function DumpIcon({ className }: { className?: string }) {
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
      <path d="M5.5 2C4 2 3 3 3 4.5v1C3 6.9 2.1 7.5 1.5 8c.6.5 1.5 1.1 1.5 2.5v1C3 13 4 14 5.5 14" />
      <path d="M10.5 2C12 2 13 3 13 4.5v1c0 1.4.9 2 1.5 2.5-.6.5-1.5 1.1-1.5 2.5v1C13 13 12 14 10.5 14" />
    </svg>
  );
}
