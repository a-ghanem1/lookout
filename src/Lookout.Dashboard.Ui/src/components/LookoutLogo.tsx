import type { SVGProps } from 'react';

export function LookoutLogo(props: SVGProps<SVGSVGElement>) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 100 100"
      role="img"
      aria-label="Lookout"
      {...props}
    >
      <path
        d="M 35 22 L 26 22 A 4 4 0 0 0 22 26 L 22 35"
        fill="none"
        stroke="currentColor"
        strokeWidth="8"
        strokeLinecap="square"
      />
      <path
        d="M 65 78 L 74 78 A 4 4 0 0 0 78 74 L 78 65"
        fill="none"
        stroke="currentColor"
        strokeWidth="8"
        strokeLinecap="square"
      />
      <circle cx="50" cy="50" r="4" fill="currentColor" />
    </svg>
  );
}
