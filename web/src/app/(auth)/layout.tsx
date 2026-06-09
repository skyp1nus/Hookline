import { BrandMark } from "@/components/brand-mark";

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-svh flex-col items-center justify-center bg-background px-4 py-10">
      <div className="mb-6 flex items-center gap-2.5">
        <BrandMark size={34} className="block" />
        <span className="text-xl font-[640] tracking-[-0.02em]">Hookline</span>
      </div>
      <div className="w-full max-w-[400px]">{children}</div>
    </div>
  );
}
