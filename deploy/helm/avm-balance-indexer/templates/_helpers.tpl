{{- define "avm.name" -}}
{{- .Chart.Name -}}
{{- end -}}

{{- define "avm.fullname" -}}
{{- printf "%s-%s" .Release.Name .Values.chain | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "avm.labels" -}}
app.kubernetes.io/name: {{ include "avm.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
avm.scholtz.sk/chain: {{ .Values.chain }}
avm.scholtz.sk/storage-provider: {{ .Values.storage.provider | lower }}
{{- end -}}

{{- define "avm.selectorLabels" -}}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: {{ .component }}
avm.scholtz.sk/chain: {{ .Values.chain }}
{{- end -}}

{{- define "avm.image" -}}
{{ .Values.image.repository }}/{{ .component }}:{{ .Values.image.tag | default .Chart.AppVersion }}
{{- end -}}
