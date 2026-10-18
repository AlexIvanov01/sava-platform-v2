{{/* Common labels. */}}
{{- define "sava.labels" -}}
app.kubernetes.io/part-of: sava
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end }}

{{/* Selector labels. Args: root, name. */}}
{{- define "sava.selectorLabels" -}}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/name: {{ .name }}
{{- end }}

{{/* Env var read from the shared Secret. Args: root, name, key. */}}
{{- define "sava.secretEnv" -}}
- name: {{ .name }}
  valueFrom:
    secretKeyRef:
      name: {{ .root.Values.secrets.name }}
      key: {{ .key }}
{{- end }}

{{/* RabbitMQ connection settings for services that publish or subscribe. */}}
{{- define "sava.rabbitmqEnv" -}}
- name: RabbitMQHost
  value: rabbitmq-clusterip-srv
- name: RabbitMQPort
  value: "5672"
{{ include "sava.secretEnv" (dict "root" . "name" "RabbitMQUser" "key" "rabbitmq-user") }}
{{ include "sava.secretEnv" (dict "root" . "name" "RabbitMQPassword" "key" "rabbitmq-password") }}
{{- end }}

{{/* Services migrate on startup, so wait for the in-cluster MySQL to accept connections. */}}
{{- define "sava.waitForMysql" -}}
{{- if .Values.mysql.enabled }}
- name: wait-for-mysql
  image: {{ .Values.waitImage }}
  command: ["sh", "-c", "until nc -z mysql 3306; do echo waiting for mysql; sleep 2; done"]
{{- end }}
{{- end }}

{{/* Connection string from values, or for the in-cluster MySQL. Args: root, value, database. */}}
{{- define "sava.connectionString" -}}
{{- if .value -}}
{{ .value }}
{{- else if .root.Values.mysql.enabled -}}
server=mysql;port=3306;database={{ .database }};user=root;password={{ required "secrets.values.mysqlRootPassword is required" .root.Values.secrets.values.mysqlRootPassword }}
{{- else -}}
{{ fail (printf "A connection string for %s is required when mysql.enabled is false" .database) }}
{{- end -}}
{{- end }}

{{/*
Deployment and Service for one Sava service. Args:
  root, name, image, service (Service name used by ocelot.json and gRPC clients), resourcesKey,
  grpc (bool), env (YAML string), initContainers (YAML string), serviceType, servicePort.
*/}}
{{- define "sava.app" -}}
{{- $root := .root -}}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ .name }}-depl
  labels:
    {{- include "sava.labels" $root | nindent 4 }}
    app.kubernetes.io/name: {{ .name }}
spec:
  replicas: 1
  selector:
    matchLabels:
      {{- include "sava.selectorLabels" . | nindent 6 }}
  template:
    metadata:
      labels:
        {{- include "sava.labels" $root | nindent 8 }}
        app.kubernetes.io/name: {{ .name }}
      {{- if $root.Values.secrets.create }}
      annotations:
        # Restart pods when the chart-managed Secret changes.
        checksum/secret: {{ include (print $root.Template.BasePath "/secret.yaml") $root | sha256sum }}
      {{- end }}
    spec:
      {{- with $root.Values.image.pullSecrets }}
      imagePullSecrets:
        {{- toYaml . | nindent 8 }}
      {{- end }}
      {{- with .initContainers }}
      initContainers:
        {{- . | nindent 8 }}
      {{- end }}
      containers:
        - name: {{ .name }}
          image: "{{ $root.Values.image.repository }}/{{ .image }}:{{ required "image.tag is required, e.g. --set image.tag=<git sha>" $root.Values.image.tag }}"
          imagePullPolicy: {{ $root.Values.image.pullPolicy }}
          ports:
            - name: http
              containerPort: 80
            {{- if .grpc }}
            - name: grpc
              containerPort: 666
            {{- end }}
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: {{ $root.Values.environment | quote }}
            {{- with .env }}
            {{- . | nindent 12 }}
            {{- end }}
          # Startup covers migrations and seeding; the app only listens once they finish.
          startupProbe:
            httpGet:
              path: /healthz
              port: http
            periodSeconds: 5
            failureThreshold: 60
          livenessProbe:
            httpGet:
              path: /healthz
              port: http
            periodSeconds: 15
            failureThreshold: 3
          readinessProbe:
            httpGet:
              path: /readyz
              port: http
            periodSeconds: 10
            failureThreshold: 3
          resources:
            {{- toYaml (index $root.Values.resources .resourcesKey) | nindent 12 }}
---
apiVersion: v1
kind: Service
metadata:
  name: {{ .service }}
  labels:
    {{- include "sava.labels" $root | nindent 4 }}
    app.kubernetes.io/name: {{ .name }}
spec:
  type: {{ .serviceType | default "ClusterIP" }}
  selector:
    {{- include "sava.selectorLabels" . | nindent 4 }}
  ports:
    - name: http
      port: {{ .servicePort | default 80 }}
      targetPort: http
    {{- if .grpc }}
    - name: grpc
      port: 666
      targetPort: grpc
    {{- end }}
{{- end }}
