#!/usr/bin/env bash
# Builds the :dev images and deploys them to Docker Desktop Kubernetes with values-dev.yaml.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: deploy/dev.sh [--reset] [service...]

Builds the images and installs or upgrades the dev release on Docker Desktop Kubernetes.

  service   auth, customer, inventory, order or gateway. Default: all of them.
  --reset   Uninstall first and delete the MySQL volume, so the databases are re-created
            and re-seeded with sample data.
EOF
}

NAMESPACE=sava
RELEASE=sava
CONTEXT=docker-desktop
ALL_SERVICES="auth customer inventory order gateway"

project_dir() {
  case "$1" in
    auth) echo AuthenticationService ;;
    customer) echo CustomerService ;;
    inventory) echo InventoryService ;;
    order) echo OrderService ;;
    gateway) echo OcelotApiGateway ;;
    *) return 1 ;;
  esac
}

reset=false
services=""
for arg in "$@"; do
  case "$arg" in
    -h | --help) usage; exit 0 ;;
    --reset) reset=true ;;
    *)
      project_dir "$arg" >/dev/null || { echo "Unknown service: $arg" >&2; usage >&2; exit 1; }
      services="$services $arg"
      ;;
  esac
done
services="${services:-$ALL_SERVICES}"

cd "$(dirname "$0")/.."
chart=deploy/helm/sava

for tool in docker helm kubectl; do
  command -v "$tool" >/dev/null || { echo "$tool is not installed." >&2; exit 1; }
done

# Never deploy development settings to another cluster by accident.
current_context=$(kubectl config current-context 2>/dev/null || true)
if [ "$current_context" != "$CONTEXT" ]; then
  echo "kubectl context is '${current_context:-none}', expected '$CONTEXT'." >&2
  echo "Run: kubectl config use-context $CONTEXT" >&2
  exit 1
fi

for service in $services; do
  dir=$(project_dir "$service")
  image="alexivanov01/$(echo "$dir" | tr '[:upper:]' '[:lower:]'):dev"
  echo "==> Building $image"
  docker build --quiet -t "$image" "backend/$dir" >/dev/null
done

if $reset; then
  echo "==> Removing the release and the MySQL volume"
  helm uninstall "$RELEASE" -n "$NAMESPACE" --wait 2>/dev/null || true
  kubectl delete pvc data-mysql-0 -n "$NAMESPACE" --ignore-not-found
fi

upgrade=false
helm status "$RELEASE" -n "$NAMESPACE" >/dev/null 2>&1 && upgrade=true

echo "==> Deploying the chart"
helm upgrade --install "$RELEASE" "$chart" -f "$chart/values-dev.yaml" \
  -n "$NAMESPACE" --create-namespace >/dev/null

# The :dev tag doesn't change, so restart the rebuilt services to pull the new images.
if $upgrade; then
  for service in $services; do
    deployment="$(project_dir "$service" | tr '[:upper:]' '[:lower:]')-depl"
    kubectl rollout restart "deployment/$deployment" -n "$NAMESPACE" >/dev/null
  done
fi

echo "==> Waiting for the pods (migrations and seeding can take a few minutes)"
for deployment in $(kubectl get deployments -n "$NAMESPACE" -o name); do
  kubectl rollout status "$deployment" -n "$NAMESPACE" --timeout=6m >/dev/null
  echo "    ${deployment#deployment.apps/} ready"
done

cat <<'EOF'

Sava is running:
  API gateway          http://localhost  (POST /api/Account with admin/admin123)
  RabbitMQ management  http://localhost:15673  (guest/guest)
  MySQL                localhost:3307  (root/sava-dev-password)
EOF
