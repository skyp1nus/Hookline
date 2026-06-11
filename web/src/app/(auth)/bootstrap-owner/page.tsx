"use client";

import { ShieldCheck } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { toast } from "sonner";

import { useBootstrapState, useCreateOwner } from "@/features/auth/hooks";
import { apiErrorMessage } from "@/lib/api/client";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export default function BootstrapOwnerPage() {
  const router = useRouter();
  const createOwner = useCreateOwner();
  const { data: bootstrap } = useBootstrapState();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  // Owner already exists → this one-time flow is over.
  useEffect(() => {
    if (bootstrap?.ownerExists) router.replace("/");
  }, [bootstrap, router]);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await createOwner.mutateAsync({ email, password });
      toast.success("Owner created.");
      router.replace("/");
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <Card>
      <CardHeader>
        <div className="mb-1 flex size-10 items-center justify-center rounded-[10px] bg-primary/15 text-primary">
          <ShieldCheck className="size-5" />
        </div>
        <CardTitle className="text-lg">Create the Owner</CardTitle>
        <CardDescription>
          The Owner is the supreme account — full control of users, roles, modules and
          connections. This can be done once; after that, only the Owner can grant the role.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="owner-email">Owner email</Label>
            <Input
              id="owner-email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="owner@hookline.io"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="owner-password">Owner password</Label>
            <Input
              id="owner-password"
              type="password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <Button type="submit" className="mt-1 w-full" disabled={createOwner.isPending}>
            {createOwner.isPending ? "Creating…" : "Create Owner"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
