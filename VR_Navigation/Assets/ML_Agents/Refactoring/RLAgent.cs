using System;
using System.Collections.Generic;
using Unity.MLAgents;
//using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using System.Collections;
using UnityEngine.Events;

public class RLAgent : Agent
{
    public action[] goalAction;
    private bool run = true;
    private int nextTargetCount = 0;

    public Group group;

    private Vector2 minMaxSpeed = MyConstants.minMaxSpeed;

    private float currentSpeed;
    private float newAngle;

    private bool fleeing = false;


    [NonSerialized] public Vector3 startPosition;
    [NonSerialized] public Quaternion startRotation;

    private Rigidbody rigidBody;

    private Animator animator;

    private Vector2 speedMaxRange = MyConstants.speedMaxRange;

    [NonSerialized] public List<GameObject> targetsTaken = new List<GameObject>();

    private AgentSensorsManager agentSensorsManager;
    private AgentGizmosDrawer agentGizmosDrawer;
    private AgentObserver agentObserver;

    private List<float> wallsAndTargetsObservations;
    private List<float> wallsAndAgentsObservations;

    private List<(GizmosTag, Vector3)> wallsAndAgents = new List<(GizmosTag, Vector3)>();
    private List<(GizmosTag, Vector3)> wallsAndTargets = new List<(GizmosTag, Vector3)>();

    public event Action<float, Environment> agentTerminated;
    public event Action<RLAgent> resetAgent;

    private string uniqueID;
    public int envStep;
    private int stepLeft;

    private int numberOfIteraction;
    private float cumulativeReward;
    public bool lockPosition;
    private int tempoIniziale;

    [NonSerialized] public Environment env;

    float entryValue;
    float exitValue;
    [NonSerialized] public string envID;

    [Serializable]
    public struct action
    {
        public GameObject goalLocation;
        public float delay;
        public String animationName;
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (goalAction.Length >= 1 && goalAction[0].goalLocation != null)
        {
            animator.SetTrigger("isWalking");
            gameObject.GetComponent<AgentSensorsManager>().invisibleTargets.Remove(goalAction[0].goalLocation);
        }
        agentSensorsManager = GetComponent<AgentSensorsManager>();
        agentGizmosDrawer = GetComponent<AgentGizmosDrawer>();
        agentObserver = GetComponent<AgentObserver>();
        startPosition = transform.position;
        startRotation = transform.rotation;
        rigidBody = GetComponent<Rigidbody>();
        agentSensorsManager.UpdateTargetSensorVision(group);
    }

    private void Update()
    {
        //print(rigidBody.velocity.magnitude);
        animator.SetFloat("Speed", rigidBody.velocity.magnitude / 10);
    }

    public override void OnEpisodeBegin()
    {
        stepLeft = envStep;
        uniqueID = Guid.NewGuid().ToString();
        rigidBody.velocity = Vector3.zero;
        tempoIniziale = (int)Time.time;
        currentSpeed = 0;
        numberOfIteraction = 0;
        minMaxSpeed.y = RandomGaussian(speedMaxRange.x, speedMaxRange.y);
        resetAgent?.Invoke(this);
    }

    public override void CollectObservations(VectorSensor vectorSensor)
    {
        Dictionary<AgentSensorsManager.Sensore, RaycastHit[]> sensorsResults = agentSensorsManager.ComputeSensorResults();

        wallsAndAgents = agentObserver.WallsAndAgentsGizmos;
        wallsAndTargets = agentObserver.WallsAndTargetsGizmos;

        (wallsAndTargetsObservations, wallsAndAgentsObservations) = agentObserver.ComputeObservations(sensorsResults);
        //wallsAndAgentsObservations = agentObserver.ComputeObservations(sensorsResults).wallsAndAgentsObservations;


        float normalizedSpeed = (currentSpeed - minMaxSpeed.x) / (minMaxSpeed.y - minMaxSpeed.x);
        agentGizmosDrawer.SetObservationsResults(wallsAndTargets, wallsAndAgents);

        vectorSensor.AddObservation(wallsAndTargetsObservations);
        vectorSensor.AddObservation(wallsAndAgentsObservations);
        vectorSensor.AddObservation(normalizedSpeed);

        rewardsWallsAndTargetsObservations(wallsAndTargets);
        rewardsWallsAndAgentsObservations(wallsAndAgents);
    }

    public override void OnActionReceived(float[] vectorAction)
    {
        if (run)
        {
            float realSpeed = rigidBody.velocity.magnitude;
            float actionSpeed;
            float actionAngle;
            if (MyConstants.discrete)
            {
                actionSpeed = vectorAction[0];
                actionSpeed = (actionSpeed - 5f) / 5f;
                actionAngle = vectorAction[1];
                actionAngle = (actionAngle - 5f) / 5f;
            }
            else
            {
                actionSpeed = Mathf.Clamp(vectorAction[0], -1f, 1f);
                actionAngle = Mathf.Clamp(vectorAction[1], -1f, 1f);

            }
            if (!lockPosition)
            {
                AngleChange(actionAngle);
                SpeedChange(actionSpeed);
            }

            int agentID = gameObject.GetInstanceID();
            numberOfIteraction++;
            StatsWriter.WriteAgentStats(
                transform.position.x,
                transform.position.z,
                group,
                currentSpeed,
                realSpeed,
                (actionAngle * MyConstants.angleRange),
                envID,
                uniqueID,
                numberOfIteraction
                );
            StatsWriter.WritePedPyStats(
                transform.position.x,
                transform.position.y,
                transform.position.z,
                uniqueID.GetHashCode());
            ComputeSteps();
        }
    }
    //public override void OnActionReceived(ActionBuffers actionBuffers)
    //{
    //    float actionSpeed = Mathf.Clamp(actionBuffers.ContinuousActions[0], -1f, 1f);
    //    float actionAngle = Mathf.Clamp(actionBuffers.ContinuousActions[1], -1f, 1f);

    //    if (!lockPosition)
    //    {
    //        SpeedChange(actionSpeed);
    //        AngleChange(actionAngle);
    //    }
    //    ComputeSteps();
    //    int agentID = gameObject.GetInstanceID();
    //    numberOfIteraction++;
    //    StatsWriter.agentStats?.Invoke(
    //        transform.position.x,
    //        transform.position.z,
    //        group,
    //        currentSpeed,
    //        (actionAngle * MyConstants.angleRange),
    //        uniqueID,
    //        numberOfIteraction
    //        );
    //}

    public void ComputeSteps()
    {
        AddReward(MyConstants.step_reward);
        stepLeft--;
        if (stepLeft <= 0)
        {
            AddReward(MyConstants.step_finished_reward);
            print("finished_step");
            Finished();
        }
    }

    public void Finished()
    {
        cumulativeReward = GetCumulativeReward();
        gameObject.SetActive(false);
        StatsWriter.WriteEnvStats(group, (int)(Time.time - tempoIniziale));
        targetsTaken.Clear();
        transform.position = startPosition;
        transform.rotation = startRotation;
        EndEpisode();
        agentTerminated?.Invoke(cumulativeReward, env);

    }

    //returns a number derived by a gaussian
    public static float RandomGaussian(float minValue = 0.0f, float maxValue = 1.0f)
    {
        float u, v, S;
        do
        {
            u = 2.0f * UnityEngine.Random.value - 1.0f;
            v = 2.0f * UnityEngine.Random.value - 1.0f;
            S = u * u + v * v;
        }
        while (S >= 1.0f);

        float std = u * Mathf.Sqrt(-2.0f * Mathf.Log(S) / S);

        float mean = (minValue + maxValue) / 2.0f;
        float sigma = (maxValue - mean) / 3.0f;
        return Mathf.Clamp(std * sigma + mean, minValue, maxValue);
    }
    IEnumerator MoveToNextTargetWithDelay(float delay)
    {
        
        if (delay>0)
        {
            run = false;
            SpeedChange(-rigidBody.velocity.magnitude);
            yield return new WaitForSeconds(delay);
        }
        run = true;
        MoveToNextTarget();
    }


    private void MoveToNextTarget()
    {
        if (goalAction.Length >= nextTargetCount + 2 && goalAction[nextTargetCount + 1].goalLocation != null)
        {
            animator.SetTrigger("isWalking");
            nextTargetCount++;
            gameObject.GetComponent<AgentSensorsManager>().invisibleTargets.Remove(goalAction[nextTargetCount].goalLocation);
        }
        else
        {
            animator.SetTrigger("isIdle");
        }
    }

    // When the agent reach the next target in the action list wait for the set time and perform the animation corresponding to the reached target
    // and set the agent to reach the following target in the list.
    private void OnTriggerEnter(Collider other)
    {
        GameObject reachedTarget = other.gameObject;
        if(!fleeing && goalAction.Length > nextTargetCount && other.gameObject == goalAction[nextTargetCount].goalLocation)
        {
            if (goalAction[nextTargetCount].animationName != "")
            {
                animator.SetTrigger(goalAction[nextTargetCount].animationName);
            }
            gameObject.GetComponent<AgentSensorsManager>().invisibleTargets.Add(goalAction[nextTargetCount].goalLocation);
            
            Target target = reachedTarget.GetComponent<Target>();
            entryValue = Vector3.Dot(transform.forward, reachedTarget.transform.forward);
            if ((target.group == group || target.group == Group.Generic) && target.targetType == TargetType.Final)
            {
                AddReward(MyConstants.finale_target_reward);
                print("final target");
                Finished();
            }

            StartCoroutine(MoveToNextTargetWithDelay(goalAction[nextTargetCount].delay));
        }
        else if (fleeing && other.gameObject.name.Contains("Flee"))
        {
            Target target = reachedTarget.GetComponent<Target>();
            entryValue = Vector3.Dot(transform.forward, reachedTarget.transform.forward);
            if ((target.group == group || target.group == Group.Generic) && target.targetType == TargetType.Final)
            {
                AddReward(MyConstants.finale_target_reward);
                print("final target");
                Finished();
            }
        }
       
    }
    private void OnTriggerExit(Collider other)
    {
        GameObject reachedTarget = other.gameObject;
        Target target = reachedTarget.GetComponent<Target>();
        exitValue = Vector3.Dot(transform.forward, reachedTarget.transform.forward);
        float resultValue = entryValue * exitValue;
        if ((target.group == Group.Generic || target.group == group) && target.targetType == TargetType.Intermediate)
        {
            if (!targetsTaken.Contains(reachedTarget))
            {
                if (resultValue >= 0)
                {
                    targetsTaken.Add(reachedTarget);
                    AddReward(MyConstants.new_target_reward);
                    print("target intermedio");
                }
                else
                {
                    AddReward(MyConstants.target_taken_incorrectly_reward);
                    print("target intermedio preso in modo scorretto");
                }

            }
            else
            {
                AddReward(MyConstants.already_taken_target_reward);
                print("already_taken_target_reward");
            }
        }
    }

    // Start the evacuation for the agent by making it follow the evac targets
    public void flee()
    {
        animator.SetTrigger("isRunning");
        animator.SetFloat("Speed", 2);
        GameObject[] fleeTargets = GameObject.FindGameObjectsWithTag("Target");
        fleeing = true;
        gameObject.GetComponent<AgentSensorsManager>().invisibleTargets.Add(goalAction[nextTargetCount - 1].goalLocation);
        foreach(GameObject target in fleeTargets)
        {
            if (target.name.Contains("Flee"))
            {
                gameObject.GetComponent<AgentSensorsManager>().invisibleTargets.Remove(target);
            }
        }
    }
    public override void Heuristic(float[] actionsOut)
    {
        //move agent by keyboard
        var continuousActionsOut = actionsOut;
        continuousActionsOut[0] = Input.GetAxis("Vertical");
        continuousActionsOut[1] = Input.GetAxis("Horizontal");
    }
    private void SpeedChange(float deltaSpeed)
    {
        currentSpeed += (minMaxSpeed.y * deltaSpeed / 2f);
        currentSpeed = Mathf.Clamp(currentSpeed, minMaxSpeed.x, minMaxSpeed.y);
        Vector3 velocityChange = (transform.forward * currentSpeed * 5) - rigidBody.velocity;
        rigidBody.AddForce(velocityChange, ForceMode.VelocityChange);
        //rigidBody.velocity = transform.forward * currentSpeed;
        //animator.SetFloat("Speed", currentSpeed);
    }

    private void AngleChange(float deltaAngle)
    {
        newAngle = Mathf.Round((deltaAngle * MyConstants.angleRange) + transform.rotation.eulerAngles.y);
        newAngle %= 360;
        if (newAngle < 0) { newAngle += 360f; }
        transform.eulerAngles = new Vector3(0, newAngle, 0);
    }

    private void rewardsWallsAndTargetsObservations(List<(GizmosTag, Vector3)> wallsAndTargets)
    {
        bool target = false;
        bool proxemic_small_wall = false;
        for (int i = 0; i < wallsAndTargets.Count; i++)
        {
            (GizmosTag wallsAndTargetTag, Vector3 wallsAndTargetVector) = wallsAndTargets[i];
            float agentAndWallsAndTargetDistance = Vector3.Distance(transform.position + Vector3.up, wallsAndTargetVector);
            if ((agentAndWallsAndTargetDistance < MyConstants.proxemic_small_distance + MyConstants.rayOffset) &&
                (wallsAndTargetTag == GizmosTag.Wall))
            {
                StatsWriter.WriteAgentCollision(
                   transform.position.x,
                   transform.position.z,
                   "Wall",
                   "Small",
                   uniqueID
                );
                proxemic_small_wall = true;
                // decommentare qua se vogliamo che la rewards della prossemica si attivi per ogni raggio
                //AddReward(MyConstants.proxemic_small_wall_reward);
                //print("proxemic_small_wall_reward");

            }
            if (wallsAndTargetTag == GizmosTag.NewTarget)
            {
                target = true;
            }
        }
        if (!target)
        {
            AddReward(MyConstants.not_watching_target_reward);
            print("not_watching_target_reward");
        }
        if (proxemic_small_wall)
        {
            AddReward(MyConstants.proxemic_small_wall_reward);
            print("proxemic_small_wall_reward");
        }
    }
    private void rewardsWallsAndAgentsObservations(List<(GizmosTag, Vector3)> wallsAndAgents)
    {
        bool proxemic_large_agent = false;
        bool proxemic_medium_agent = false;
        bool proxemic_small_agent = false;

        for (int i = 0; i < wallsAndAgents.Count; i++)
        {
            (GizmosTag wallsAndAgentsTag, Vector3 wallsAndAgentsVector) = wallsAndAgents[i];
            float agentAndWallsAndAgentsDistance = Vector3.Distance(transform.position + Vector3.up, wallsAndAgentsVector);

            if ((MyConstants.proxemic_large_distance + MyConstants.rayOffset >= agentAndWallsAndAgentsDistance)
                && (MyConstants.proxemic_medium_distance + MyConstants.rayOffset < agentAndWallsAndAgentsDistance) &&
                (wallsAndAgentsTag == GizmosTag.Agent) && (i < (MyConstants.proxemic_large_ray * 2) + 1))
            {
                StatsWriter.WriteAgentCollision(
                   transform.position.x,
                   transform.position.z,
                   "Agent",
                   "Large",
                   uniqueID
                );
                proxemic_large_agent = true;
                // decommentare qua se vogliamo che la rewards della prossemica si attivi per ogni raggio

                //AddReward(MyConstants.proxemic_large_agent_reward);
                //print("proxemic_large_agent_reward");
            }
            else if ((MyConstants.proxemic_medium_distance + MyConstants.rayOffset >= agentAndWallsAndAgentsDistance)
               && (MyConstants.proxemic_small_distance + MyConstants.rayOffset < agentAndWallsAndAgentsDistance) &&
               (wallsAndAgentsTag == GizmosTag.Agent) && (i < (MyConstants.proxemic_medium_ray * 2) + 1))
            {
                StatsWriter.WriteAgentCollision(
                   transform.position.x,
                   transform.position.z,
                   "Agent",
                   "Medium",
                   uniqueID
                );
                proxemic_medium_agent = true;
                // decommentare qua se vogliamo che la rewards della prossemica si attivi per ogni raggio

                //AddReward(MyConstants.proxemic_medium_agent_reward);
                //print("proxemic_medium_agent_reward");

            }
            else if ((MyConstants.proxemic_small_distance + MyConstants.rayOffset >= agentAndWallsAndAgentsDistance) &&
                (wallsAndAgentsTag == GizmosTag.Agent))
            {
                StatsWriter.WriteAgentCollision(
                   transform.position.x,
                   transform.position.z,
                   "Agent",
                   "Small",
                   uniqueID
                );
                proxemic_small_agent = true;
                // decommentare qua se vogliamo che la rewards della prossemica si attivi per ogni raggio

                //AddReward(MyConstants.proxemic_small_agent_reward);
                //print("proxemic_small_agent_reward");

            }
        }
        if (proxemic_small_agent)
        {
            AddReward(MyConstants.proxemic_small_agent_reward);
            print("proxemic_small_agent_reward");
        }
        else if (proxemic_medium_agent)
        {
            AddReward(MyConstants.proxemic_medium_agent_reward);
            print("proxemic_medium_agent_reward");
        }
        else if (proxemic_large_agent)
        {
            AddReward(MyConstants.proxemic_large_agent_reward);
            print("proxemic_large_agent_reward");
        }


    }

}
