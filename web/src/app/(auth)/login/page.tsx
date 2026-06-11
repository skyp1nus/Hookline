"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

import { useLogin, type BootstrapState } from "@/features/auth/hooks";
import { api, apiErrorMessage } from "@/lib/api/client";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export default function LoginPage() {
  const router = useRouter();
  const login = useLogin();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const me = await login.mutateAsync({ email, password });
      // Route a fresh deployment straight into the one-time Create-Owner flow.
      const bootstrap = await api.get<BootstrapState>("/auth/bootstrap-state");
      const needsOwner = !bootstrap.ownerExists && me.role === "Admin";
      router.replace(needsOwner ? "/bootstrap-owner" : "/");
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Sign in to Hookline</CardTitle>
        <CardDescription>Use your hub account to continue.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="email">Email</Label>
            <Input
              id="email"
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="you@hookline.io"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="password">Password</Label>
            <Input
              id="password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <Button type="submit" className="mt-1 w-full" disabled={login.isPending}>
            {login.isPending ? "Signing in…" : "Sign in"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
