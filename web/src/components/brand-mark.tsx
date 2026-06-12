import Image from "next/image";

/** Hookline brand mark — the app icon (a hook on a line, webhook motif). */
export function BrandMark({
  size = 28,
  className,
}: {
  size?: number;
  className?: string;
}) {
  return (
    <Image
      src="/hookline-icon.png"
      alt="Hookline"
      width={size}
      height={size}
      priority
      className={className}
    />
  );
}
