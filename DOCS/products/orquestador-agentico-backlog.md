# Orquestador Agéntico de Backlog (nombre de trabajo)

> Borrador de propuesta de producto. Redactado a partir de una descripción
> verbal, ampliado con lo ya implementado en este repo como v1 (punto 6) y
> con el proceso de referencia documentado en el `CONTRIBUTING.md` de
> ds-connect (punto 4).

## 1. Resumen ejecutivo

Una capa de abstracción sobre la parte de **backlog** de cualquier proveedor
(Azure DevOps, GitHub, GitLab) para el seguimiento de historias de usuario.
El producto detecta, de forma **configurable**, cuándo una historia de
usuario entra en cierto estado — usando el mecanismo nativo de cada vendor
(labels en GitHub, estados de work item en Azure DevOps, labels/estado de
issue en GitLab) — y dispara una serie de **acciones con IA** en respuesta a
esa transición de estado.

La ejecución ocurre en un contenedor Docker que trae **OpenCode, GitHub CLI
y Claude Code instalados a la vez**; por configuración se decide qué
runtime/instalación se usa en cada despliegue, sin que eso sea un fork
arquitectónico. Cada vez que una historia entra en el estado configurado, se
levanta un **"AI pod" sobre Azure Container Apps** (un job scale-to-zero, no
un pod de Kubernetes/AKS) que ejecuta esa implementación en modo *loop*, sin
intervención humana salvo que el propio workflow la requiera.

## 2. Problema que resuelve

Hoy el proceso de "grillado → propuesta → implementación → revisión" de
historias de usuario es automatizable por diseño (hay comandos internos como
`/propose` o `/implement`) pero no está automatizado: un humano tiene que
fijarse en la etiqueta/estado y lanzar el agente manualmente. Además, cada
equipo/cliente puede vivir en un vendor de backlog distinto (Azure DevOps,
GitHub, GitLab), y hoy cualquier automatización de este tipo se acopla a uno
solo de ellos.

## 3. Qué es el producto

- **Capa de abstracción de backlog**: una interfaz común de "historia de
  usuario + estado" por encima de Azure DevOps, GitHub y GitLab. Cada vendor
  aporta su propio mecanismo de notificación de cambio de estado (webhook de
  issues, webhook de work item, webhook de issues/labels de GitLab) y su
  propio mecanismo de "estado" (label vs. work item state vs. label/estado
  de issue); el producto normaliza esto a un modelo único.
- **Motor de estados configurable**: quién decide qué estado de la historia
  dispara qué acción de IA no es fijo — es configuración. Esto es lo que
  hoy, en la v1 de este mismo repo, está *hardcodeado* a dos labels de
  ds-connect (`status:ready-for-proposal`, `status:ready-for-implementation`)
  y sería el primer punto a generalizar.
- **Imagen de agente multi-runtime**: un Docker con OpenCode, `gh` (GitHub
  CLI) y Claude Code instalados los tres a la vez; por configuración se
  decide con cuál de las tres instalaciones se ejecuta cada despliegue/tarea.
- **Despachador sobre Azure Container Apps**: cada vez que una historia
  entra en el estado configurado se levanta un "AI pod" — un Container Apps
  Job scale-to-zero — que clona el repo, ejecuta la instrucción configurada
  para ese estado, y deja la historia en el siguiente estado del workflow.

## 4. Caso de uso de referencia (workflow con estados)

El workflow de referencia es el ciclo spec-first de ds-connect
(`CONTRIBUTING.md` de ese repo): nueve estados (`status:*` labels) con dos
**gates de máquina** y dos **revisiones humanas** (HITL) sobre un único
branch/PR por historia.

```
idea ─grill─▶ needs-refinement ─(resolver gaps)─▶ ready-for-proposal
   (gate DoR)                                          │
                                                 propose (gate LLM)
                                                       ▼
                                        proposal-review ─(humano valida)─▶ ready-for-implementation
                                        draft PR · HITL #1                     (gate LLM, WIP ≤ 4)
                                                                                      │
   done ◀─sync─ code-review ◀─implement─ in-progress ◀───────────────────────────────┘
                same PR · HITL #2
```

1. **Grill** (`/ds:grill`) — interroga la idea/issue contra la Definition of
   Ready; si no la cumple → `needs-refinement` con los gaps comentados; si
   la cumple → `ready-for-proposal`.
2. **Ready for proposal** — *gate de máquina*: al entrar en este estado
   salta el loop agéntico: se levanta el AI pod, clona el repo y ejecuta el
   comando interno `/ds:propose`. El agente crea la propuesta (OpenSpec
   change) en una rama, abre un **draft PR** y deja la historia en
   `proposal-review`.
3. **Proposal review** (HITL #1) — un humano revisa/corrige la spec en el
   draft PR. Si pide cambios y re-aplica el gate label, el agente se
   re-despacha y revisa usando los comentarios del PR. Si valida, mueve la
   historia a `ready-for-implementation`.
4. **Ready for implementation** — *gate de máquina* (con límite WIP de 4
   concurrentes): salta de nuevo el AI pod, que ejecuta `/ds:implement`
   sobre la **misma rama/PR**, marca el PR como ready y deja la historia en
   `code-review`.
5. **Code review** (HITL #2) — revisión humana del código en el mismo PR;
   igual que en HITL #1, "request changes" + re-label re-despacha al agente.
6. **Sync** (`/ds:sync`, humano) — cierre: retro, archivo de specs,
   squash-merge de un commit limpio a main → `done`.

Además existe `status:blocked`, alcanzable desde cualquier estado — es donde
el agente deja la historia si una ejecución falla, con un comentario del
contexto del fallo.

La generalización de producto: estos estados, gates y comandos son la
configuración de *un* cliente/repo. El producto permite definir el mapeo
"estado → acción agéntica" para cualquier workflow equivalente en cualquier
vendor.

## 5. Arquitectura (propuesta, a validar)

```
Vendor de backlog (Azure DevOps | GitHub | GitLab)
        │  evento nativo (webhook: work item updated / issue labeled / issue estado)
        ▼
Adaptador de vendor  (uno por proveedor)
  - normaliza el evento a {repo, item_id, estado_canónico}
  - aplica el mapeo configurable "estado nativo → estado canónico → acción"
        │
        ▼
Cola de trabajo  (Storage Queue, como en la v1 actual)
        │
        ▼
Azure Container Apps Job ("ai pod", KEDA scale 0..N por profundidad de cola)
  - una ejecución = un "AI pod" por historia en el estado configurado
  - selecciona runtime del agente (OpenCode | Claude Code | gh) por config
  - clona el repo, ejecuta la instrucción del estado, actualiza el estado
    de la historia en el vendor de origen al terminar
```

## 6. Relación con lo ya implementado en este repo

Este mismo repositorio (`aca_loop`) ya implementa una **v1 de este producto,
acoplada a un único vendor y a un único cliente**: ver [`README.md`](../../README.md)
y [`openspec/changes/ai-loop-aca-job/`](../../openspec/changes/ai-loop-aca-job/).

Lo que ya existe hoy:

| Pieza | Estado actual (v1, solo GitHub / ds-connect) | Generalización que pide este producto |
|---|---|---|
| Detección de estado | Webhook de GitHub `issues`, solo `action=="labeled"`, dos labels hardcodeadas | Adaptador por vendor + mapeo de estados configurable |
| Despacho | Azure Container Apps Job + KEDA sobre Storage Queue | Mismo patrón ("AI pod" = ACA Job), generalizado a multi-vendor/multi-cliente en vez de a un único repo |
| Imagen de agente | `agent/Dockerfile`: git, `gh`, Claude Code y opencode ya conviven en la misma imagen | Ya cumple el requisito de "las tres instalaciones a la vez"; falta exponer la selección de runtime como config de producto, no solo como detalle interno |
| Comandos | `/ds:propose` y `/ds:implement`, fijos, propios de ds-connect | Instrucción por estado, configurable por cliente/repo |
| Vendors soportados | Solo GitHub | Azure DevOps y GitLab quedan por construir (adaptador + credenciales + mapeo de estados nativos) |

Es decir: la parte más difícil (contrato del agente, manejo de tokens de
corta duración, contrato de fallo a "blocked", imagen multi-runtime) ya está
resuelta y probada en producción para un caso concreto. El trabajo de
producto es **extraer la capa de abstracción de vendor y de configuración de
estados**, manteniendo el despachador sobre ACA Jobs tal como está.

## 7. Preguntas abiertas

- El despachador es **Azure Container Apps** (no AKS): cada disparo es un
  "AI pod" = una ejecución de Container Apps Job, tal como ya funciona en la
  v1. Queda por definir si el mismo entorno ACA sirve para todos los
  clientes/repos (multi-tenant) o si cada cliente tiene su propio entorno
  ACA aislado.
- ¿La configuración de mapeo de estados vive por repo (como hoy, un
  `config.yaml` en el propio repo target) o pasa a ser un recurso central
  del producto (multi-cliente, multi-repo)?
- ¿Qué pasa con el estado de la historia en el vendor si el Job falla? Hoy
  la v1 lo resuelve moviendo a `status:blocked` + comentario; hay que
  definir el equivalente en Azure DevOps (work item state) y GitLab.
