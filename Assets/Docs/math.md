## Rotation representation: axis-angle to 6D

axis-angle to 6D is a two-step composition: **axis-angle → rotation matrix → 6D**.

### Step 1: axis-angle → rotation matrix (`aa2matrot`)

The input $\mathbf{v} \in \mathbb{R}^3$ encodes a rotation as axis × angle. Let

$$\theta = \lVert \mathbf{v} \rVert, \qquad \hat{\mathbf{k}} = \frac{\mathbf{v}}{\theta} = (k_x, k_y, k_z)$$

so $\theta$ is the rotation angle and $\hat{\mathbf{k}}$ the unit rotation axis. The matrix comes from the **Rodrigues formula**:

$$R = I + \sin\theta\,[\hat{\mathbf{k}}]_\times + (1-\cos\theta)\,[\hat{\mathbf{k}}]_\times^2$$

where $[\hat{\mathbf{k}}]_\times$ is the skew-symmetric cross-product matrix

$$[\hat{\mathbf{k}}]_\times = \begin{bmatrix} 0 & -k_z & k_y \\ k_z & 0 & -k_x \\ -k_y & k_x & 0 \end{bmatrix}$$

This yields $R \in SO(3)$, a $3\times3$ orthonormal matrix whose columns $\mathbf{r}_1, \mathbf{r}_2, \mathbf{r}_3$ are the rotated basis vectors.

### Step 2: rotation matrix → 6D (`matrot2sixd`, line 24)

The code takes the **first two columns** and stacks them (line 29):

$$\text{6D} = \big[\, \mathbf{r}_1 \;\Vert\; \mathbf{r}_2 \,\big] = \big[R_{:,0} ,\; R_{:,1}\big] \in \mathbb{R}^6$$

The third column is dropped because it's redundant: $\mathbf{r}_3 = \mathbf{r}_1 \times \mathbf{r}_2$ can be recovered by the cross product. This is the Zhou et al. "continuous 6D representation" — the inverse (`sixd2matrot`, line 43) reconstructs $R$ via **Gram–Schmidt**:

$$\mathbf{b}_1 = \frac{\mathbf{r}_1}{\lVert \mathbf{r}_1\rVert}, \quad \mathbf{b}_2 = \frac{\mathbf{r}_2 - (\mathbf{b}_1\!\cdot\!\mathbf{r}_2)\,\mathbf{b}_1}{\lVert \cdot \rVert}, \quad \mathbf{b}_3 = \mathbf{b}_1 \times \mathbf{b}_2$$

### Summary

$$\mathbf{v}\in\mathbb{R}^3 \;\xrightarrow{\text{Rodrigues}}\; R\in SO(3) \;\xrightarrow{\text{drop col 3}}\; [\mathbf{r}_1 \Vert \mathbf{r}_2]\in\mathbb{R}^6$$

The point of 6D over axis-angle: it's a **continuous, singularity-free** parameterization of $SO(3)$, which is why it's used as the network's rotation target (axis-angle wraps at $2\pi$ and is discontinuous, hurting regression).