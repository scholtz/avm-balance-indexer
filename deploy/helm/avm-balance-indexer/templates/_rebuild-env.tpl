{{/*
Database change requests that only the worker acts on (ADR 0020, ADR 0021): the keyspaces to retire, the keyspace the API reads while the worker
fills a new one, and the partial rebuild. They are env entries of the worker, not part of the shared ConfigMap: the API would otherwise see
`ForkFrom:Keyspace` equal to its own keyspace (when it still reads the old one) and refuse to start.
The checks repeat the options validator (CassandraOptionsValidator): a wrong value must fail at render time, not in a pod that cannot start.
Go's $ anchors at the end of the text, like \z in .NET.
*/}}
{{- define "avm.workerRebuildEnv" -}}
{{- $c := .Values.storage.cassandra -}}
{{- $pattern := "^[A-Za-z][A-Za-z0-9_]{0,47}$" -}}
{{- if and $c.retiredKeyspaces (ne .Values.storage.provider "Cassandra") }}
{{- fail "storage.cassandra.retiredKeyspaces is only carried out for storage.provider=Cassandra; the list would be ignored" }}
{{- end }}
{{- range $i, $retired := $c.retiredKeyspaces }}
{{- if not (regexMatch $pattern (toString $retired)) }}
{{- fail (printf "storage.cassandra.retiredKeyspaces holds %q, which is not a keyspace name (a letter, then letters, digits and underscores, at most 48 characters)" (toString $retired)) }}
{{- end }}
{{- if hasPrefix "system" (lower $retired) }}
{{- fail (printf "storage.cassandra.retiredKeyspaces holds %q, a Cassandra system keyspace" $retired) }}
{{- end }}
{{- if eq (lower $retired) (lower $.Values.storage.cassandra.keyspace) }}
{{- fail (printf "storage.cassandra.retiredKeyspaces holds %q, the keyspace this release uses: it would drop its own data" $retired) }}
{{- end }}
{{- if and $c.apiKeyspace (eq (lower $retired) (lower $c.apiKeyspace)) }}
{{- fail (printf "storage.cassandra.retiredKeyspaces holds %q, the keyspace the API reads (storage.cassandra.apiKeyspace): it would drop the data that is served" $retired) }}
{{- end }}
- name: Storage__Cassandra__RetiredKeyspaces__{{ $i }}
  value: {{ $retired | quote }}
{{- end }}
{{- if $c.apiKeyspace }}
{{- if ne .Values.storage.provider "Cassandra" }}
{{- fail "storage.cassandra.apiKeyspace is only used for storage.provider=Cassandra" }}
{{- end }}
{{- if not (regexMatch $pattern (toString $c.apiKeyspace)) }}
{{- fail (printf "storage.cassandra.apiKeyspace %q is not a keyspace name (a letter, then letters, digits and underscores, at most 48 characters)" (toString $c.apiKeyspace)) }}
{{- end }}
- name: Storage__Cassandra__ApiKeyspace
  value: {{ $c.apiKeyspace | quote }}
{{- end }}
{{- if $c.forkFrom.keyspace }}
{{- $fork := $c.forkFrom }}
{{- if ne $.Values.storage.provider "Cassandra" }}
{{- fail "storage.cassandra.forkFrom is only carried out for storage.provider=Cassandra" }}
{{- end }}
{{- if not (regexMatch $pattern (toString $fork.keyspace)) }}
{{- fail (printf "storage.cassandra.forkFrom.keyspace %q is not a keyspace name (a letter, then letters, digits and underscores, at most 48 characters)" (toString $fork.keyspace)) }}
{{- end }}
{{- if hasPrefix "system" (lower $fork.keyspace) }}
{{- fail (printf "storage.cassandra.forkFrom.keyspace %q is a Cassandra system keyspace" $fork.keyspace) }}
{{- end }}
{{- if eq (lower $fork.keyspace) (lower $c.keyspace) }}
{{- fail (printf "storage.cassandra.forkFrom.keyspace %q is the keyspace this release fills (storage.cassandra.keyspace)" $fork.keyspace) }}
{{- end }}
{{- range $c.retiredKeyspaces }}
{{- if eq (lower (toString .)) (lower $fork.keyspace) }}
{{- fail (printf "storage.cassandra.forkFrom.keyspace %q is also in storage.cassandra.retiredKeyspaces, which drops it" $fork.keyspace) }}
{{- end }}
{{- end }}
{{- if not (and (kindIs "float64" $fork.round) (gt $fork.round 0.0) (eq $fork.round (floor $fork.round))) }}
{{- if not (and (kindIs "int64" $fork.round) (gt (int64 $fork.round) 0)) }}
{{- fail (printf "storage.cassandra.forkFrom.round %v must be a positive integer (the last round that is known to be good)" $fork.round) }}
{{- end }}
{{- end }}
{{- if ne (lower (toString $fork.into)) (lower $c.keyspace) }}
{{- fail (printf "storage.cassandra.forkFrom.into %q must name the keyspace this release fills (storage.cassandra.keyspace %q), so that a request left in the values cannot fill another one" (toString $fork.into) $c.keyspace) }}
{{- end }}
- name: Storage__Cassandra__ForkFrom__Keyspace
  value: {{ $fork.keyspace | quote }}
- name: Storage__Cassandra__ForkFrom__Round
  value: {{ int64 $fork.round | quote }}
- name: Storage__Cassandra__ForkFrom__Into
  value: {{ $fork.into | quote }}
{{- end }}
{{- end -}}

{{/*
What the API gets while it reads another keyspace than the worker fills (a partial rebuild, ADR 0021): that keyspace, and no new holder scans
(a scan would be stored where no worker looks). An explicit env entry wins over the same name from the shared ConfigMap.
*/}}
{{- define "avm.apiRebuildEnv" -}}
{{- $c := .Values.storage.cassandra -}}
{{- if and $c.apiKeyspace (ne (lower $c.apiKeyspace) (lower $c.keyspace)) }}
- name: Storage__Cassandra__Keyspace
  value: {{ $c.apiKeyspace | quote }}
- name: Api__ScansEnabled
  value: "false"
{{- end }}
{{- end -}}
