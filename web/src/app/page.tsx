import { redirect } from "next/navigation";

export default function Home() {
  // The shell sends an unauthenticated visitor on to /login; there is no third thing the root
  // could usefully be.
  redirect("/dashboard");
}
