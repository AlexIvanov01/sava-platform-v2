{{/* Schemas included in backups and checked by the test restore. */}}
{{- define "sava.backup.databases" -}}
savadb_auth savadb_cust savadb_inv savadb_order
{{- end }}

{{/* rclone remote "backup:" from env vars. Args: root, role (writer or reader). */}}
{{- define "sava.backup.rcloneEnv" -}}
{{- $s3 := .root.Values.backup.s3 -}}
- name: RCLONE_CONFIG_BACKUP_TYPE
  value: s3
- name: RCLONE_CONFIG_BACKUP_PROVIDER
  value: {{ $s3.provider | quote }}
- name: RCLONE_CONFIG_BACKUP_ENDPOINT
  value: {{ required "backup.s3.endpoint is required when backup.enabled is true" $s3.endpoint | quote }}
{{- with $s3.region }}
- name: RCLONE_CONFIG_BACKUP_REGION
  value: {{ . | quote }}
{{- end }}
# The bucket already exists; the keys aren't allowed to create or check it.
- name: RCLONE_CONFIG_BACKUP_NO_CHECK_BUCKET
  value: "true"
{{ include "sava.secretEnv" (dict "root" .root "name" "RCLONE_CONFIG_BACKUP_ACCESS_KEY_ID" "key" (printf "backup-%s-key-id" .role)) }}
{{ include "sava.secretEnv" (dict "root" .root "name" "RCLONE_CONFIG_BACKUP_SECRET_ACCESS_KEY" "key" (printf "backup-%s-key" .role)) }}
- name: BUCKET
  value: {{ required "backup.s3.bucket is required when backup.enabled is true" $s3.bucket | quote }}
{{- end }}

{{/*
Pod that dumps every schema and uploads the files to <prefix>/<UTC timestamp>/. Args: root, kind
(nightly: daily/, plus monthly/ on the 1st and yearly/ on 1 January in backup.timeZone;
pre-deploy: pre-deploy/). Uploads only, so the writer key needs no delete permission.
*/}}
{{- define "sava.backup.dumpPod" -}}
{{- $b := .root.Values.backup -}}
restartPolicy: Never
initContainers:
  - name: dump
    image: {{ .root.Values.mysql.image }}
    command:
      - bash
      - -c
      - |
        set -euo pipefail
        for db in $DATABASES; do
          echo "--> Dumping $db"
          mysqldump -h mysql -u root --single-transaction --routines --triggers \
            --set-gtid-purged=OFF --databases "$db" | gzip > "/backup/$db.sql.gz"
        done
        ls -l /backup
    env:
      - name: DATABASES
        value: {{ include "sava.backup.databases" . | quote }}
      {{- include "sava.secretEnv" (dict "root" .root "name" "MYSQL_PWD" "key" "mysql-root-password") | nindent 6 }}
    resources:
      {{- toYaml $b.resources | nindent 6 }}
    volumeMounts:
      - name: backup
        mountPath: /backup
containers:
  - name: upload
    image: {{ $b.image }}
    command:
      - sh
      - -c
      - |
        set -eu
        stamp=$(date -u +%Y-%m-%dT%H%M%SZ)
        upload() {
          lock=""
          if [ -n "$LOCK_MODE" ]; then
            lock="--s3-object-lock-mode $LOCK_MODE --s3-object-lock-retain-until-date $2"
          fi
          echo "--> Uploading to $1/$stamp/"
          # No listing or HEAD requests: the writer key may only upload.
          rclone copy /backup "backup:$BUCKET/$1/$stamp" --no-check-dest --s3-no-head $lock
        }
        if [ "$KIND" = pre-deploy ]; then
          upload pre-deploy "$RETAIN_PRE_DEPLOY"
        else
          upload daily "$RETAIN_DAILY"
          if [ "$(date +%d)" = 01 ]; then upload monthly "$RETAIN_MONTHLY"; fi
          if [ "$(date +%m%d)" = 0101 ]; then upload yearly "$RETAIN_YEARLY"; fi
        fi
    env:
      - name: KIND
        value: {{ .kind }}
      - name: TZ
        value: {{ $b.timeZone | quote }}
      - name: LOCK_MODE
        value: {{ $b.objectLock.mode | quote }}
      - name: RETAIN_DAILY
        value: {{ $b.objectLock.retention.daily | quote }}
      - name: RETAIN_MONTHLY
        value: {{ $b.objectLock.retention.monthly | quote }}
      - name: RETAIN_YEARLY
        value: {{ $b.objectLock.retention.yearly | quote }}
      - name: RETAIN_PRE_DEPLOY
        value: {{ $b.objectLock.retention.preDeploy | quote }}
      {{- include "sava.backup.rcloneEnv" (dict "root" .root "role" "writer") | nindent 6 }}
    resources:
      {{- toYaml $b.resources | nindent 6 }}
    volumeMounts:
      - name: backup
        mountPath: /backup
volumes:
  - name: backup
    emptyDir: {}
{{- end }}
