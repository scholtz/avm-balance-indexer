{{/* Secret-backed environment variables for the selected provider only. */}}
{{- define "avm.storageEnv" -}}
{{- if eq .Values.storage.provider "PostgreSql" }}
- name: Storage__PostgreSql__ConnectionString
  valueFrom:
    secretKeyRef:
      name: {{ required "storage.postgreSql.existingSecret is required" .Values.storage.postgreSql.existingSecret }}
      key: connectionString
{{- else if eq .Values.storage.provider "Cassandra" }}
- name: Storage__Cassandra__Username
  valueFrom:
    secretKeyRef:
      name: {{ required "storage.cassandra.existingSecret is required" .Values.storage.cassandra.existingSecret }}
      key: username
- name: Storage__Cassandra__Password
  valueFrom:
    secretKeyRef:
      name: {{ .Values.storage.cassandra.existingSecret }}
      key: password
{{- else }}
{{- fail (printf "unsupported storage.provider %q" .Values.storage.provider) }}
{{- end }}
{{- if .Values.algorand.existingSecret }}
- name: Algorand__AlgodToken
  valueFrom:
    secretKeyRef:
      name: {{ .Values.algorand.existingSecret }}
      key: token
{{- end }}
{{- end -}}
